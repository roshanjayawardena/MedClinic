using Appointments;
using Asp.Versioning;
using Billing;
using Clinics;
using Core;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Finbuckle.MultiTenant.Extensions;
using OpenTelemetry.Metrics;
using Hangfire;
using Hangfire.PostgreSql;
using HealthChecks.NpgSql;
using MedClinic.Api;
using MedClinic.Api.Infrastructure;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Notifications;
using Notifications.Infrastructure;
using Encounters;
using Identity;
using Identity.Middleware;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Patients;
using Prescriptions;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using Web;

// Bootstrap logger catches startup exceptions before full Serilog config is read.
#pragma warning disable CA1305 // Serilog's Console sink has no IFormatProvider overload
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();
#pragma warning restore CA1305

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Aspire service defaults ────────────────────────────────────────────────
    // Wires OpenTelemetry (traces + metrics + OTLP via OTEL_EXPORTER_OTLP_ENDPOINT),
    // HTTP resilience handlers, service discovery, and a liveness health check.
    builder.AddServiceDefaults();

    // ── Serilog ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((context, services, config) =>
    {
        config
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();

        // Application Insights sink — active when connection string is configured.
        // Uses TelemetryConfiguration directly to avoid the Microsoft.ApplicationInsights.AspNetCore
        // 3.x dependency conflict with Serilog.Sinks.ApplicationInsights (requires AI < 3.0.0).
        var aiConnStr = context.Configuration["ApplicationInsights:ConnectionString"];
        if (!string.IsNullOrEmpty(aiConnStr))
        {
            var telemetryConfig = TelemetryConfiguration.CreateDefault();
            telemetryConfig.ConnectionString = aiConnStr;
            config.WriteTo.ApplicationInsights(telemetryConfig, new TraceTelemetryConverter());
        }
    });

    // ── Core services ─────────────────────────────────────────────────────────
    builder.Services.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Scoped);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<ITenantContext, FinbuckleTenantContext>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<ClinicMetrics>();

    // ── Finbuckle multi-tenancy ───────────────────────────────────────────────
    // Header strategy reads X-Tenant-Id; DatabaseClinicStore validates against the clinics table.
    builder.Services
        .AddMultiTenant<ClinicTenantInfo>()
        .WithHeaderStrategy("X-Tenant-Id")
        .WithStore<DatabaseClinicStore>(ServiceLifetime.Scoped);

    // ── CORS ──────────────────────────────────────────────────────────────────
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? ["http://localhost:3000"];

    builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

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

    builder.Services.AddAuthorization(options =>
    {
        // SystemAdmin: no clinic_id claim required — used for cross-tenant management endpoints.
        options.AddPolicy("SystemAdmin", policy =>
            policy.RequireRole(Identity.Domain.Roles.SystemAdmin));
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

    // ── Modules ───────────────────────────────────────────────────────────────
    var modules = new IModule[]
    {
        new ClinicsModule(),
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

    // ── Notification sender — override module default based on credentials ────
    // Modules register ConsoleNotificationSender as default; last registration wins.
    if (!string.IsNullOrEmpty(builder.Configuration["SendGrid:ApiKey"]))
        builder.Services.AddScoped<INotificationSender, SendGridEmailSender>();
    else if (!string.IsNullOrEmpty(builder.Configuration["Email:SmtpHost"]))
        builder.Services.AddScoped<INotificationSender, MailKitEmailSender>();

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
    // AddDefaultHealthChecks() (called via AddServiceDefaults) adds a "self" liveness check.
    // Here we add the postgres "ready" check; redis is conditional.
    var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"]!;
    var healthChecks = builder.Services
        .AddHealthChecks()
        .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

    if (!string.IsNullOrEmpty(redisConnection))
        healthChecks.AddRedis(redisConnection, name: "valkey", tags: ["ready"]);

    // ── OpenTelemetry — extend Aspire defaults with clinic-specific signals ────
    // AddServiceDefaults() has already registered AspNetCore + Http instrumentation.
    // We extend here: runtime metrics + custom clinic business meter.
    builder.Services.AddOpenTelemetry()
        .WithMetrics(metrics => metrics
            .AddRuntimeInstrumentation()
            .AddMeter(ClinicMetrics.MeterName));

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

    // Health endpoints exposed via Aspire-standard /health/live and /health/ready.
    app.MapDefaultEndpoints();

    app.UseCors();                  // must be before auth and tenant resolution
    app.UseHttpsRedirection();
    app.UseRateLimiter();
    app.UseMiddleware<IdempotencyMiddleware>();
    app.UseMultiTenant();           // resolves X-Tenant-Id → ClinicTenantInfo before auth
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
