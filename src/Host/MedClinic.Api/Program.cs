using Appointments;
using Asp.Versioning;
using Billing;
using Core;
using Hangfire;
using Hangfire.PostgreSql;
using HealthChecks.NpgSql;
using MedClinic.Api.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Notifications;
using Encounters;
using Identity;
using Identity.Middleware;
using MedClinic.Api;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Patients;
using Prescriptions;
using Scalar.AspNetCore;
using Serilog;
using Web;

// Bootstrap logger catches startup exceptions before full Serilog config is read.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((context, services, config) =>
        config
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

    // ── Core services ─────────────────────────────────────────────────────────
    builder.Services.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Scoped);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<ITenantContext, HttpTenantContext>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<ClinicMetrics>();

    // ── RFC 9457 ProblemDetails ───────────────────────────────────────────────
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    // ── API versioning ────────────────────────────────────────────────────────
    builder.Services
        .AddApiVersioning(options =>
        {
            options.DefaultApiVersion  = new ApiVersion(1);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions  = true;
            options.ApiVersionReader   = ApiVersionReader.Combine(
                new HeaderApiVersionReader("api-version"),
                new QueryStringApiVersionReader("api-version"));
        });

    builder.Services.AddOpenApi();

    // ── HybridCache on Valkey (Redis-compatible) ──────────────────────────────
    var redisConnection = builder.Configuration["Redis:ConnectionString"];
    if (!string.IsNullOrEmpty(redisConnection))
    {
        builder.Services.AddStackExchangeRedisCache(opts =>
            opts.Configuration = redisConnection);
    }
    else
    {
        builder.Services.AddDistributedMemoryCache();
    }
    builder.Services.AddHybridCache();

    // ── Idempotency middleware ────────────────────────────────────────────────
    builder.Services.AddTransient<IdempotencyMiddleware>();

    // ── Storage (MinIO / S3) ──────────────────────────────────────────────────
    if (!string.IsNullOrEmpty(builder.Configuration["Storage:Endpoint"]))
        builder.Services.AddSingleton<IStorageService, MinioStorageService>();

    // ── Email (MailKit — production) / Console (development) ─────────────────
    // MailKitEmailSender is wired in NotificationsModule for the INotificationSender slot.
    // In dev the ConsoleNotificationSender stub is used automatically via module registration.

    // ── Modules ───────────────────────────────────────────────────────────────
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

    // ── Hangfire ──────────────────────────────────────────────────────────────
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(opts =>
            opts.UseNpgsqlConnection(builder.Configuration["ConnectionStrings:DefaultConnection"]!),
            new PostgreSqlStorageOptions { SchemaName = "hangfire" }));
    builder.Services.AddHangfireServer();

    // ── Health checks ─────────────────────────────────────────────────────────
    var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"]!;
    var healthChecks = builder.Services
        .AddHealthChecks()
        .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

    if (!string.IsNullOrEmpty(redisConnection))
        healthChecks.AddRedis(redisConnection, name: "valkey", tags: ["ready"]);

    // ── OpenTelemetry — traces, metrics, and logs ─────────────────────────────
    var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
    builder.Services
        .AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("MediClinic", serviceVersion: "1.0"))
        .WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation(opts =>
                {
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
        })
        .WithLogging(logs =>
        {
            // Route Serilog-structured logs to OTLP so they appear alongside traces
            // in Grafana / Jaeger. PHI scrubbing happens in Serilog before this sink.
            if (!string.IsNullOrEmpty(otlpEndpoint))
                logs.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        });

    var app = builder.Build();

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseExceptionHandler();  // GlobalExceptionHandler → RFC 9457 ProblemDetails

    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} → {StatusCode} in {Elapsed:0.0}ms";
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
        app.UseHangfireDashboard("/hangfire");
    }

    // Health endpoints — before auth so load balancers can probe without a token.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResultStatusCodes =
        {
            [HealthStatus.Healthy]   = StatusCodes.Status200OK,
            [HealthStatus.Degraded]  = StatusCodes.Status200OK,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
        }
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResultStatusCodes =
        {
            [HealthStatus.Healthy]   = StatusCodes.Status200OK,
            [HealthStatus.Degraded]  = StatusCodes.Status200OK,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
        }
    });

    app.UseHttpsRedirection();
    app.UseRateLimiter();
    app.UseMiddleware<IdempotencyMiddleware>();
    app.UseAuthentication();
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
