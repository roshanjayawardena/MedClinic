# Part 12: Observability — Serilog, OpenTelemetry, and Clinic-Specific Metrics

*Building a MediClinic SaaS — Part 12 of an ongoing series*

---

Eleven parts in, the system works — appointments book, reminders fire, invoices generate. But you can't tell. Every request produces the same default ASP.NET Core log line:

```
info: Microsoft.AspNetCore.Routing.EndpointMiddleware[1]
      Executed endpoint 'HTTP: POST /appointments'
```

No duration. No status code in the log body. No business context. No idea whether reminders are being sent or silently failing. No signal for brute-force login attempts. Nothing an oncall engineer could act on at 2 AM.

This part adds three layers of observability:

1. **Serilog** — structured logging replacing the default console output, configured entirely from `appsettings.json`, with a file sink and per-namespace level overrides
2. **Health checks** — two endpoints (`/health/live`, `/health/ready`) that load balancers and Kubernetes can use to know whether the process is alive and whether it can serve traffic
3. **OpenTelemetry** — distributed traces and metrics exported via OTLP, with custom business counters for the things a clinic actually needs to watch

---

## Why structured logging matters for a medical system

In a medical SaaS, two things are simultaneously true:

1. You need rich diagnostic context to debug problems quickly
2. Every log line is a potential PHI leak

The tension is real. A naive developer adds `logger.LogWarning("Login failed for {Email}", command.Email)` and you've just put patient email addresses in your log aggregator. A paranoid developer adds no logging at all, and you spend three hours debugging an outage by reading source code.

Structured logging resolves this by separating *what happened* (safe to log) from *who it happened to* (PHI, never log). The properties attached to each log event are typed, indexed, and filterable — which is only possible if you use a structured logging framework.

Serilog has been the standard for .NET structured logging for years. In .NET 10, `Serilog.AspNetCore` integrates it cleanly with the `Microsoft.Extensions.Logging` abstraction that the rest of the framework already uses.

---

## Serilog setup

### Bootstrap logger

The very first thing in `Program.cs`, before `WebApplication.CreateBuilder`:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    // ...
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "MediClinic host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
```

The bootstrap logger exists to catch exceptions during startup — before the full Serilog configuration is read from `appsettings.json`. Without it, a bad connection string or missing configuration key fails silently or produces a generic .NET exception with no structured context.

`HostAbortedException` is excluded from the catch because it's thrown by EF Core tooling (`dotnet ef migrations add`) to signal a clean shutdown, not a crash.

The `finally` block flushes any buffered log events before the process exits. Without it, the last few log lines can be lost.

### Wiring into the host

```csharp
builder.Host.UseSerilog((context, services, config) =>
    config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());
```

`ReadFrom.Configuration` loads everything from `appsettings.json` — sinks, minimum levels, enrichers, output templates. This means you can change log levels in production without a redeploy.

`ReadFrom.Services` allows Serilog sinks that need DI-resolved services (like custom sinks that write to a database). It's not used here yet, but it's free and avoids a config change later.

`Enrich.FromLogContext` enables `LogContext.PushProperty(...)` — used in middleware to attach per-request properties like `TenantId` to every log event in that request.

### Configuration in `appsettings.json`

```json
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.File" ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning",
        "Hangfire": "Warning",
        "System": "Warning"
      }
    },
    "Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId" ],
    "Properties": {
      "Application": "MediClinic"
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/mediclinic-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 14,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  }
}
```

The namespace overrides are the most important part. Without them:
- EF Core logs every SQL query at `Debug` — overwhelming in production
- Hangfire logs every job poll cycle — produces hundreds of lines per minute
- `Microsoft.*` logs internal routing and middleware details — noise

With the overrides, you see only what your own code logs at `Information` and above. Noisy frameworks are silenced to `Warning`.

The file sink rolls daily, keeps 14 days, and uses absolute timestamps including timezone offset — essential for correlating logs across servers or debugging daylight saving issues.

### Development overrides in `appsettings.Development.json`

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft": "Information",
        "Microsoft.EntityFrameworkCore.Database.Command": "Information",
        "Hangfire": "Information"
      }
    }
  }
}
```

In development, you want to see EF Core's generated SQL (`Database.Command` at `Information`) and Hangfire job activity. These override the production-safe defaults only in the `Development` environment.

### Request logging

```csharp
app.UseSerilogRequestLogging(opts =>
{
    opts.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} → {StatusCode} in {Elapsed:0.0}ms";
    opts.GetLevel = (ctx, _, _) =>
        ctx.Request.Path.StartsWithSegments("/health")
            ? Serilog.Events.LogEventLevel.Verbose
            : Serilog.Events.LogEventLevel.Information;
});
```

This replaces ASP.NET Core's built-in request logging with a single structured event per request, emitted after the response is sent. The default ASP.NET Core request logs produce two events per request (request received + endpoint executed) and have no duration information.

Health check endpoints are suppressed to `Verbose` — they run every 10 seconds from load balancers and would otherwise flood the logs.

The result for a booking request looks like:

```
[14:23:01 INF] Serilog.AspNetCore.RequestLoggingMiddleware:
  HTTP POST /appointments → 200 in 47.3ms
```

One line. Duration included. Structured so you can query `StatusCode > 400` in Seq or Grafana Loki.

---

## Health checks

Health checks answer two different questions that are often confused:

- **Liveness**: "Is the process running?" — if no, the container runtime should restart it
- **Readiness**: "Can the process serve traffic?" — if no, the load balancer should stop sending it requests

These are separate because a process can be alive but not ready — for example, if it's still running EF Core migrations at startup, or if the database has briefly gone away.

```csharp
builder.Services
    .AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);
```

The `"ready"` tag means this check is included in the readiness endpoint but not the liveness endpoint.

```csharp
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,   // no actual checks — just "am I running?"
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    }
});
```

`/health/live` runs no checks — if the process responds at all, it's alive. `/health/ready` runs the PostgreSQL probe.

The NpgSql health check (`AspNetCore.HealthChecks.NpgSql`) opens a connection and executes `SELECT 1`. If it fails, the check reports `Unhealthy` and the endpoint returns HTTP 503.

Both endpoints are mapped **before** `UseAuthentication()`. Load balancers probe health endpoints without authentication tokens — they need to work even when the auth middleware is broken.

### What to add next

The PostgreSQL check is the minimum. In a production deployment, you'd add:

```csharp
builder.Services
    .AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"])
    .AddHangfire(opts => opts.MinimumAvailableServers = 1, tags: ["ready"])
    .AddDiskStorageHealthCheck(opts => opts.AddDrive("C:\\", 512), name: "disk", tags: ["ready"]);
```

Hangfire's health check verifies at least one server is processing jobs. The disk check catches log directories running out of space before they cause write failures.

---

## OpenTelemetry

OpenTelemetry is the vendor-neutral standard for distributed observability. You configure it once with the OTLP exporter, then point it at whatever backend you prefer: Jaeger for traces, Prometheus + Grafana for metrics, or a commercial platform like Honeycomb or Datadog.

### Setup

```csharp
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
```

The OTLP endpoint is configured in `appsettings.json`:

```json
{
  "OpenTelemetry": {
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

Leave it empty in environments without a collector — the exporter just won't be registered and no telemetry is sent. The application starts and runs normally either way.

Health and Hangfire dashboard requests are filtered out of traces. Without this filter, every load-balancer health probe produces a span — which is noise in the trace backend and adds up in cost on volume-priced services.

### Traces

`AddAspNetCoreInstrumentation()` produces a span for every HTTP request, including:
- `http.method`, `http.route`, `http.status_code`
- Duration
- Error information if the request throws

`AddHttpClientInstrumentation()` adds spans for outbound HTTP calls — which matters if you ever integrate Twilio for SMS or any other HTTP-based provider. You'll see the outbound call as a child span of the booking request.

In development, `AddConsoleExporter()` prints traces to the console:

```
Activity.DisplayName: POST /appointments
Activity.Duration:    00:00:00.0523847
Activity.Tags:
    http.request.method: POST
    http.response.status_code: 200
    server.address: localhost
    server.port: 5001
```

This is verbose but extremely useful when debugging locally without a trace backend.

### Custom metrics: `ClinicMetrics`

The `System.Diagnostics.Metrics` API — built into the .NET runtime, no packages needed — provides a `Meter` class that OpenTelemetry reads automatically when you call `.AddMeter(name)`.

```csharp
// src/BuildingBlocks/Core/ClinicMetrics.cs
public sealed class ClinicMetrics : IDisposable
{
    public const string MeterName = "MediClinic";

    private readonly Meter _meter;

    public Counter<long> AppointmentsBooked { get; }
    public Counter<long> AppointmentsCancelled { get; }
    public Counter<long> NotificationsScheduled { get; }
    public Counter<long> NotificationsSent { get; }
    public Counter<long> NotificationsFailed { get; }
    public Counter<long> NotificationsConsentDenied { get; }
    public Counter<long> LoginSuccess { get; }
    public Counter<long> LoginFailed { get; }

    public ClinicMetrics()
    {
        _meter = new Meter(MeterName, "1.0");

        AppointmentsBooked = _meter.CreateCounter<long>(
            "mediclinic.appointments.booked",
            description: "Total appointments booked");
        AppointmentsCancelled = _meter.CreateCounter<long>(
            "mediclinic.appointments.cancelled");
        NotificationsScheduled = _meter.CreateCounter<long>(
            "mediclinic.notifications.scheduled");
        NotificationsSent = _meter.CreateCounter<long>(
            "mediclinic.notifications.sent");
        NotificationsFailed = _meter.CreateCounter<long>(
            "mediclinic.notifications.failed");
        NotificationsConsentDenied = _meter.CreateCounter<long>(
            "mediclinic.notifications.consent_denied");
        LoginSuccess = _meter.CreateCounter<long>(
            "mediclinic.auth.login.success");
        LoginFailed = _meter.CreateCounter<long>(
            "mediclinic.auth.login.failed",
            description: "Failed login attempts — monitor for brute-force patterns");
    }

    public void Dispose() => _meter.Dispose();
}
```

Registered as a singleton in `Program.cs`:

```csharp
builder.Services.AddSingleton<ClinicMetrics>();
```

Injected wherever a business event should be recorded. This class lives in `Core` because the handlers that need it span multiple modules.

---

## Placing metrics in handlers

### Login — the security signal

```csharp
public sealed class LoginHandler(
    UserManager<ClinicUser> userManager,
    IJwtService jwtService,
    IConfiguration configuration,
    ClinicMetrics metrics,
    ILogger<LoginHandler> logger)
    : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    public async ValueTask<Result<LoginResponse>> Handle(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(command.Email).ConfigureAwait(false);

        if (user is null || !user.IsActive)
        {
            // Never log command.Email — it is PHI.
            logger.LogWarning("Login failed: {Reason}", "UserNotFound");
            metrics.LoginFailed.Add(1);
            return Result<LoginResponse>.Fail(
                new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        var passwordValid = await userManager
            .CheckPasswordAsync(user, command.Password)
            .ConfigureAwait(false);

        if (!passwordValid)
        {
            logger.LogWarning("Login failed: {Reason}", "InvalidPassword");
            metrics.LoginFailed.Add(1);
            return Result<LoginResponse>.Fail(
                new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        var roles = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        var token = jwtService.GenerateToken(user, roles);
        var expiresIn = configuration.GetValue<int>("Jwt:ExpiryMinutes", 60) * 60;

        logger.LogInformation("Login succeeded: Role={Role}", string.Join(',', roles));
        metrics.LoginSuccess.Add(1);

        return Result<LoginResponse>.Ok(new LoginResponse(token, "Bearer", expiresIn));
    }
}
```

Three things to notice:

**The email is never logged.** `command.Email` is PHI. The log line says `"UserNotFound"` — which tells an engineer everything they need to diagnose a login failure without exposing who was trying to log in.

**The roles are safe to log.** `Doctor`, `Pharmacist`, `Receptionist`, `Admin` are organizational roles, not personal identifiers.

**`metrics.LoginFailed` is a brute-force signal.** Set an alert in Grafana or your APM tool: if `mediclinic.auth.login.failed` exceeds 10 in a 60-second window, page someone. This is the first line of defense against credential stuffing.

### Booking — the business signal

```csharp
// At the end of BookAppointmentHandler.Handle(), after SaveChangesAsync:
metrics.AppointmentsBooked.Add(1);
return Result<BookAppointmentResponse>.Ok(new BookAppointmentResponse(appointment.Id));
```

One line. `mediclinic.appointments.booked` over time is the fundamental business metric for this clinic. You can graph it hourly to see booking patterns, compare week-over-week, or alert when bookings drop to zero (which might mean the booking form is broken).

### Reminder job — the operational signal

```csharp
// In AppointmentReminderJob:
if (!contact.ConsentToCommunications)
{
    record.MarkConsentDenied();
    await db.SaveChangesAsync().ConfigureAwait(false);
    metrics.NotificationsConsentDenied.Add(1);
    return;
}

try
{
    await sender.SendAsync(..., CancellationToken.None).ConfigureAwait(false);
    record.MarkSent(timeProvider.GetUtcNow());
    metrics.NotificationsSent.Add(1);
}
catch (Exception ex)
{
    logger.LogError("Appointment reminder SMS failed: {ExceptionType}", ex.GetType().Name);
    record.MarkFailed(ex.GetType().Name);
    metrics.NotificationsFailed.Add(1);
}
```

`mediclinic.notifications.failed` is the SMS delivery error rate. When you switch from `ConsoleNotificationSender` to the real Twilio sender, this metric tells you immediately if the API key is wrong, the number format is invalid, or Twilio is having an outage — without logging a single phone number.

---

## The PHI logging rules, summarized

These rules are in `.agents/rules/phi-and-tenancy.md` and enforced by the `phi-review` workflow. The observability layer makes them more important, not less — every log sink is a new place PHI could end up.

| What to log | What not to log |
|---|---|
| Error codes (`Auth.InvalidCredentials`) | Emails, phone numbers, names |
| Exception type (`ex.GetType().Name`) | `ex.Message` (may contain PHI from providers) |
| Outcome categories (`UserNotFound`) | Any field from `command` that is patient data |
| Roles (`Doctor`, `Pharmacist`) | User IDs (can be correlated to individuals) |
| Metric increments | Patient IDs in log messages |
| Request method and path | Route parameter values (e.g., `/patients/{id}`) |

The last rule is subtle: the request logging template uses `{RequestPath}` which logs the raw path including route parameter values. For routes like `/patients/{id}/encounters`, the patient ID appears in the log. This is acceptable at `Debug` level but consider scrubbing it for production logs at `Information`.

---

## Connecting to a backend

### Local development — traces to console

With `AddConsoleExporter()` in development, every request prints its trace to the console. No Docker, no Jaeger, no setup.

### Jaeger for traces

Run Jaeger locally:

```bash
docker run -d --name jaeger \
  -p 16686:16686 \
  -p 4317:4317 \
  jaegertracing/all-in-one:latest
```

Set in `appsettings.Development.json`:

```json
{
  "OpenTelemetry": {
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

Traces appear at `http://localhost:16686`. You can search by `service.name = MediClinic`, drill into any request, see the SQL queries as child spans (if you add EF Core instrumentation), and measure p99 latency.

### Prometheus + Grafana for metrics

The OTLP exporter works with the OpenTelemetry Collector, which can scrape and forward to Prometheus. A minimal collector config:

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317

exporters:
  prometheus:
    endpoint: 0.0.0.0:8889

service:
  pipelines:
    metrics:
      receivers: [otlp]
      exporters: [prometheus]
```

Prometheus scrapes `localhost:8889` and the metrics are available in Grafana. The counter names from `ClinicMetrics` become Prometheus metric names:

```
mediclinic_appointments_booked_total
mediclinic_auth_login_failed_total
mediclinic_notifications_sent_total
```

A Grafana dashboard showing these four panels tells you everything about the clinic's operational health at a glance:
- Booking volume (is the system being used?)
- Failed logins (is anyone attacking it?)
- Reminder sent rate vs. consent denied rate (is consent blocking reminders?)
- Notification failure rate (is the SMS provider working?)

---

## What changed

| Layer | Change |
|---|---|
| `Core` | `ClinicMetrics` — `System.Diagnostics.Metrics` Meter with 8 counters |
| `MedClinic.Api` | Serilog via `UseSerilog()` + bootstrap logger; `UseSerilogRequestLogging()`; health checks at `/health/live` and `/health/ready`; OpenTelemetry traces + metrics with OTLP exporter |
| `appsettings.json` | Full Serilog configuration (Console + File sinks, level overrides, enrichers); OTLP endpoint setting |
| `appsettings.Development.json` | Debug level, EF Core SQL logging enabled |
| `LoginHandler` | `LogWarning` on failure (no PHI), `LogInformation` on success; increments `LoginFailed` / `LoginSuccess` |
| `BookAppointmentHandler` | Increments `AppointmentsBooked` |
| `CancelAppointmentHandler` | Increments `AppointmentsCancelled` |
| `OnAppointmentBookedHandler` | Increments `NotificationsScheduled` |
| `AppointmentReminderJob` | Increments `NotificationsSent`, `NotificationsFailed`, or `NotificationsConsentDenied` |

---

## What's next

The clinic is now fully observable. The next natural area is **deployment**: how do you run this system in production? That means containerisation (a `Dockerfile`, a `docker-compose.yml` that brings up PostgreSQL, the migrator, and the API), and a look at the `MedClinic.DbMigrator` project that runs migrations at deploy time rather than startup. That's Part 13.

---

*The code for this article is in the [MediClinic repository](https://github.com) on branch `master`, commit tagged `article/part-12`. All seven modules, the test suite, the AI agent layer, Hangfire background jobs, and the full observability stack are there.*
