using Appointments;
using Billing;
using Core;
using Hangfire;
using Hangfire.PostgreSql;
using HealthChecks.NpgSql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Notifications;
using Encounters;
using Identity;
using Identity.Middleware;
using MedClinic.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Patients;
using Prescriptions;
using Scalar.AspNetCore;
using Serilog;

// Bootstrap logger catches startup exceptions before full Serilog config is read.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, config) =>
        config
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

    // ServiceLifetime.Scoped allows handlers to consume scoped dependencies (UserManager,
    // ICurrentUser, INotificationSender). Singleton is Mediator's default but is incompatible
    // with ASP.NET Core Identity services which are always registered as Scoped.
    builder.Services.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Scoped);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<ITenantContext, HttpTenantContext>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<ClinicMetrics>();

    builder.Services.AddOpenApi();

    var modules = new IModule[]
    {
        new PatientsModule(),
        new AppointmentsModule(),
        new EncountersModule(),
        new PrescriptionsModule(),
        new IdentityModule(),
        new BillingModule(),
        new NotificationsModule(),
    };

    foreach (var module in modules)
        module.RegisterServices(builder.Services, builder.Configuration);

    // Hangfire — PostgreSQL-backed job storage + in-process server.
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(opts =>
            opts.UseNpgsqlConnection(builder.Configuration["ConnectionStrings:DefaultConnection"]!),
            new PostgreSqlStorageOptions { SchemaName = "hangfire" }));
    builder.Services.AddHangfireServer();

    // Health checks — /health/live (liveness) and /health/ready (readiness + DB probe).
    var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"]!;
    builder.Services
        .AddHealthChecks()
        .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

    // OpenTelemetry — traces and metrics exported via OTLP (Jaeger, Prometheus, Grafana, etc.).
    var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
    builder.Services
        .AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(
            serviceName: "MediClinic",
            serviceVersion: "1.0"))
        .WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation(opts =>
                {
                    // Exclude health check and Hangfire dashboard requests from traces.
                    opts.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments("/health") &&
                        !ctx.Request.Path.StartsWithSegments("/hangfire");
                })
                .AddHttpClientInstrumentation();

            if (!string.IsNullOrEmpty(otlpEndpoint))
                tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));

            if (builder.Environment.IsDevelopment())
                tracing.AddConsoleExporter();
        })
        .WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter(ClinicMetrics.MeterName);

            if (!string.IsNullOrEmpty(otlpEndpoint))
                metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        });

    var app = builder.Build();

    // Serilog request logging replaces the default ASP.NET Core request logs
    // with a single structured line per request at completion.
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} → {StatusCode} in {Elapsed:0.0}ms";
        // Exclude health check endpoints from request logs — they're too noisy.
        opts.GetLevel = (ctx, _, _) =>
            ctx.Request.Path.StartsWithSegments("/health")
                ? Serilog.Events.LogEventLevel.Verbose
                : Serilog.Events.LogEventLevel.Information;
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.Title = "MediClinic API";
            options.Theme = ScalarTheme.Purple;
        });
        // Hangfire dashboard — dev-only; restrict to authenticated admins in production.
        app.UseHangfireDashboard("/hangfire");
    }

    // Health endpoints — excluded from auth so load balancers can probe without a token.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,     // liveness: no checks — if process responds it's alive
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status200OK,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
        }
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),   // readiness: DB must be reachable
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status200OK,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
        }
    });

    app.UseHttpsRedirection();
    app.UseRateLimiter();
    app.UseAuthentication();
    // Validates JWT clinic_id == X-Tenant-Id header — prevents cross-clinic data access.
    app.UseMiddleware<TenantClaimValidationMiddleware>();
    app.UseAuthorization();

    foreach (var module in modules)
        module.MapEndpoints(app);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "MediClinic host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
