# Part 11: Scheduled SMS Reminders with Hangfire — Solving Multi-Tenant Background Jobs

*Building a MediClinic SaaS — Part 11 of an ongoing series*

---

Ten parts in, the system sends an SMS reminder the moment a patient books an appointment. That works — but it's wrong. An appointment booked three weeks in advance should produce a reminder 24 hours *before* it happens, not three weeks early. And if the patient cancels, that reminder job should disappear.

This part adds real scheduled reminders using Hangfire. It's a short implementation — maybe 400 lines of new code — but it exposes a non-obvious architectural problem that any multi-tenant background job system will hit: your HTTP-based tenant context breaks the moment you leave the request thread.

Let's solve that cleanly.

---

## What we're building

When a patient books an appointment:
1. Hangfire schedules a job to fire 24 hours before the appointment
2. A `Scheduled` notification record is written with the Hangfire job ID
3. At fire time, the job looks up patient contact details, checks consent, and sends the SMS
4. If the appointment is cancelled before the job fires, the job is deleted and the notification is marked `Cancelled`

If the appointment is booked less than 24 hours away, the job is enqueued immediately rather than scheduled.

---

## The multi-tenant background job problem

The system uses an `ITenantContext` abstraction to scope every database query to the current clinic. The implementation that runs in production is `HttpTenantContext`:

```csharp
public sealed class HttpTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    public Guid TenantId
    {
        get
        {
            var header = httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].ToString();
            if (!Guid.TryParse(header, out var tenantId))
                throw new InvalidOperationException("No tenant could be resolved for the current request.");

            return tenantId;
        }
    }
}
```

This reads the `X-Tenant-Id` request header. When the request ends, `HttpContext` becomes null. When a Hangfire worker thread picks up the job minutes or hours later, `HttpContext` is always null — so `HttpTenantContext` throws `InvalidOperationException` on the very first database call.

This is the fundamental problem: background jobs are multi-tenant but live outside the HTTP request. Every EF query goes through the tenant filter, which reads `ITenantContext.TenantId`, which reads `HttpContext` — which doesn't exist.

---

## The solution: `BackgroundJobTenantScope`

The fix is an `AsyncLocal<Guid>` — a value that is scoped to the current async execution context. Jobs set it before running. `HttpTenantContext` checks it first.

```csharp
// src/BuildingBlocks/Core/BackgroundJobTenantScope.cs
public static class BackgroundJobTenantScope
{
    private static readonly AsyncLocal<Guid> _tenantId = new();

    public static Guid Current
    {
        get => _tenantId.Value;
        set => _tenantId.Value = value;
    }

    public static bool IsActive => _tenantId.Value != Guid.Empty;
}
```

`AsyncLocal<T>` propagates into child tasks (it flows down the call chain) but does not leak back up. Setting it in a Hangfire worker thread only affects that thread's async context — other jobs running concurrently on other threads are unaffected.

Then update `HttpTenantContext` to check the background scope before trying the HTTP header:

```csharp
public Guid TenantId
{
    get
    {
        // Hangfire jobs set BackgroundJobTenantScope before executing.
        if (BackgroundJobTenantScope.IsActive)
            return BackgroundJobTenantScope.Current;

        var header = httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].ToString();
        if (!Guid.TryParse(header, out var tenantId))
            throw new InvalidOperationException("No tenant could be resolved for the current request.");

        return tenantId;
    }
}
```

This is the entire multi-tenancy fix: two files, twelve lines of new code. Every EF query that flows through the DI-resolved `ITenantContext` now works correctly in both HTTP requests and background jobs.

---

## Why `AsyncLocal` and not something else?

**Thread-static fields** would be simpler to write but break with `async/await` — the continuation may run on a different thread, but `[ThreadStatic]` doesn't follow it.

**Passing tenant as a constructor argument to the job** would work but forces every handler the job calls (including cross-module Mediator queries) to accept a tenant parameter, which isn't how they're designed.

**Creating DbContext instances manually** inside the job, bypassing the DI-registered `IDbContextFactory<T>`, would work but means duplicating the tenant filtering logic everywhere.

`AsyncLocal<T>` is the right tool because it behaves like ambient state that flows naturally through async code, which is exactly how HTTP context already works.

---

## Adding Hangfire

Three packages, two projects:

```xml
<!-- Notifications/Notifications.csproj -->
<PackageReference Include="Hangfire.Core" Version="1.8.23" />
<PackageReference Include="Hangfire.PostgreSql" Version="1.21.1" />

<!-- MedClinic.Api/MedClinic.Api.csproj -->
<PackageReference Include="Hangfire.AspNetCore" Version="1.8.23" />
<PackageReference Include="Hangfire.PostgreSql" Version="1.21.1" />
```

The Notifications module uses `IBackgroundJobClient` to schedule jobs and needs `Hangfire.Core`. The API host needs `Hangfire.AspNetCore` for the server and dashboard, and `Hangfire.PostgreSql` for storage configuration.

`Hangfire.PostgreSql` stores its tables in a dedicated `hangfire` schema. It creates them automatically on first startup — no EF migration needed for Hangfire's own infrastructure.

---

## Wiring Hangfire in `Program.cs`

```csharp
// Hangfire — PostgreSQL-backed job storage + in-process server.
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(opts =>
        opts.UseNpgsqlConnection(builder.Configuration["ConnectionStrings:DefaultConnection"]!),
        new PostgreSqlStorageOptions { SchemaName = "hangfire" }));

builder.Services.AddHangfireServer();
```

And the dashboard, dev-only:

```csharp
if (app.Environment.IsDevelopment())
{
    // ...
    app.UseHangfireDashboard("/hangfire");
}
```

In production, the dashboard should be secured — at minimum with `DashboardOptions.Authorization` pointing to an admin-only filter. For this series we leave it open in development and document the production concern.

---

## Domain changes

### NotificationStatus

Two new states:

```csharp
public enum NotificationStatus
{
    Scheduled,      // Hangfire job queued — reminder not yet sent
    Sent,
    Failed,
    ConsentDenied,
    Cancelled,      // Appointment cancelled; job deleted before firing
}
```

`Scheduled` replaces what was previously a gap: a notification was written immediately as `Sent` or `Failed` at booking time. Now it's written as `Scheduled` and updated by the job when it runs.

### Notification entity

Add `HangfireJobId` and behaviour methods:

```csharp
public string? HangfireJobId { get; private set; }

public void MarkSent(DateTimeOffset sentAt)
{
    Status = NotificationStatus.Sent;
    SentAt = sentAt;
    HangfireJobId = null;
}

public void MarkFailed(string reason)
{
    Status = NotificationStatus.Failed;
    FailureReason = reason;
}

public void MarkConsentDenied()
{
    Status = NotificationStatus.ConsentDenied;
}

public void MarkCancelled()
{
    Status = NotificationStatus.Cancelled;
    HangfireJobId = null;
}
```

The `HangfireJobId` is cleared on terminal states — it's only needed while the job is pending.

The `Record()` factory gains an optional parameter:

```csharp
public static Notification Record(
    Guid patientId,
    Guid? appointmentId,
    NotificationChannel channel,
    string templateKey,
    NotificationStatus status,
    DateTimeOffset? sentAt = null,
    string? failureReason = null,
    string? hangfireJobId = null) => new() { ... };
```

---

## The EF migration

```
dotnet ef migrations add AddNotificationHangfireJobId \
  --project src/Host/MedClinic.Migrations.PostgreSQL \
  --startup-project src/Host/MedClinic.Migrations.PostgreSQL \
  --context NotificationsDbContext
```

The generated migration adds a single nullable `text` column:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "HangfireJobId",
        schema: "notifications",
        table: "notifications",
        type: "text",
        nullable: true);
}
```

---

## The Hangfire job

`AppointmentReminderJob` is a plain class registered as `AddScoped<AppointmentReminderJob>()`. Hangfire resolves it from DI when it fires, which means it gets `IDbContextFactory<NotificationsDbContext>`, `IMediator`, and `INotificationSender` injected normally.

```csharp
public sealed class AppointmentReminderJob(
    IDbContextFactory<NotificationsDbContext> dbFactory,
    IMediator mediator,
    INotificationSender sender,
    TimeProvider timeProvider,
    ILogger<AppointmentReminderJob> logger)
{
    public async Task SendAsync(
        Guid appointmentId,
        Guid patientId,
        Guid clinicId,
        DateTimeOffset scheduledAt)
    {
        // Restore tenant context for this background execution.
        BackgroundJobTenantScope.Current = clinicId;

        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);

        var record = await db.Notifications
            .SingleOrDefaultAsync(n =>
                n.AppointmentId == appointmentId &&
                n.TemplateKey == TemplateKeys.AppointmentReminder)
            .ConfigureAwait(false);

        // Idempotency: already sent, or appointment cancelled after job was scheduled.
        if (record is null ||
            record.Status is NotificationStatus.Sent or NotificationStatus.Cancelled)
            return;

        var contactResult = await mediator
            .Send(new GetPatientContactQuery(patientId))
            .ConfigureAwait(false);

        if (contactResult.IsFailure)
        {
            logger.LogWarning("Reminder job: GetPatientContact failed: {Code}",
                contactResult.Error!.Code);
            record.MarkFailed(contactResult.Error.Code);
            await db.SaveChangesAsync().ConfigureAwait(false);
            return;
        }

        var contact = contactResult.Value;

        if (!contact.ConsentToCommunications)
        {
            record.MarkConsentDenied();
            await db.SaveChangesAsync().ConfigureAwait(false);
            return;
        }

        var body = $"Reminder: your appointment is on " +
                   $"{scheduledAt.LocalDateTime:dddd d MMMM yyyy 'at' h:mm tt}. " +
                   $"Please arrive 10 minutes early.";

        try
        {
            // contact.ContactPhone is PHI — passed to sender, never logged.
            await sender
                .SendAsync(
                    new NotificationMessage(NotificationChannel.Sms, contact.ContactPhone, body),
                    CancellationToken.None)
                .ConfigureAwait(false);
            record.MarkSent(timeProvider.GetUtcNow());
        }
        catch (Exception ex)
        {
            // Log only exception type — ex.Message may contain the phone number.
            logger.LogError("Appointment reminder SMS failed: {ExceptionType}", ex.GetType().Name);
            record.MarkFailed(ex.GetType().Name);
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }
}
```

A few things worth calling out:

**`BackgroundJobTenantScope.Current = clinicId` is the first line.** Before any database call, the tenant scope is set. If this line is missing or placed after a DB call, the query filter reads an empty GUID and returns nothing.

**Idempotency check before any network call.** Hangfire has at-least-once delivery semantics. If the job is retried (for example, the host crashed after the SMS sent but before `SaveChangesAsync` completed), the check prevents a duplicate SMS. The patient gets at most one reminder.

**`CancellationToken.None` on the sender.** Hangfire does not thread a `CancellationToken` through serialized job expressions. The method signature accepts none; the internal sender call uses `CancellationToken.None`. This is explicit and intentional, not an oversight.

**PHI in the exception handler.** Only `ex.GetType().Name` is logged, never `ex.Message`. SMS providers sometimes include the destination number in exception messages. One stray `ex.Message` log is a PHI leak.

---

## Scheduling the job: `OnAppointmentBookedHandler`

The booking handler is rewritten to schedule rather than send:

```csharp
public sealed class OnAppointmentBookedHandler(
    IDbContextFactory<NotificationsDbContext> dbFactory,
    IBackgroundJobClient backgroundJobs,
    TimeProvider timeProvider)
    : INotificationHandler<AppointmentBookedIntegrationEvent>
{
    private static readonly TimeSpan ReminderLeadTime = TimeSpan.FromHours(24);

    public async ValueTask Handle(
        AppointmentBookedIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Idempotency guard — at-least-once delivery assumption.
        var alreadyHandled = await db.Notifications
            .AnyAsync(n =>
                n.AppointmentId == notification.AppointmentId &&
                n.TemplateKey == TemplateKeys.AppointmentReminder,
                cancellationToken)
            .ConfigureAwait(false);

        if (alreadyHandled)
            return;

        var now = timeProvider.GetUtcNow();
        var reminderAt = notification.ScheduledAt - ReminderLeadTime;

        // Schedule 24 h before the appointment; enqueue immediately if < 24 h away.
        string hangfireJobId;
        if (reminderAt > now)
        {
            hangfireJobId = backgroundJobs.Schedule<AppointmentReminderJob>(
                job => job.SendAsync(
                    notification.AppointmentId,
                    notification.PatientId,
                    notification.ClinicId,
                    notification.ScheduledAt),
                reminderAt);
        }
        else
        {
            hangfireJobId = backgroundJobs.Enqueue<AppointmentReminderJob>(
                job => job.SendAsync(
                    notification.AppointmentId,
                    notification.PatientId,
                    notification.ClinicId,
                    notification.ScheduledAt));
        }

        db.Notifications.Add(Notification.Record(
            notification.PatientId,
            notification.AppointmentId,
            NotificationChannel.Sms,
            TemplateKeys.AppointmentReminder,
            NotificationStatus.Scheduled,
            hangfireJobId: hangfireJobId));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

The handler no longer injects `IMediator`, `INotificationSender`, or `ILogger`. All of that logic moved to the job. The handler's only jobs now are: schedule the Hangfire task and record that it did so.

`IBackgroundJobClient` is a Hangfire singleton — safe to inject into a Scoped handler.

Notice that `notification.ClinicId` is passed into the job expression. Hangfire serializes this to JSON (the job storage record). When the job fires, these four parameters are deserialized from storage and passed to `SendAsync`. The tenant ID travels safely through Hangfire's persistence layer — no ambient context crosses the process boundary.

---

## Cancellation: two new pieces

### `AppointmentCancelledIntegrationEvent`

```csharp
public sealed record AppointmentCancelledIntegrationEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ClinicId,
    DateTimeOffset CancelledAt,
    DateTimeOffset OccurredAt) : INotification;
```

### `CancelAppointmentCommand` and endpoint

The `Appointment` aggregate already had a `Cancel(string reason, DateTimeOffset now)` method. All it needed was a Mediator command wired to it:

```csharp
public sealed class CancelAppointmentHandler(
    IDbContextFactory<AppointmentsDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    IMediator mediator)
    : IRequestHandler<CancelAppointmentCommand, Result<CancelAppointmentResponse>>
{
    public async ValueTask<Result<CancelAppointmentResponse>> Handle(
        CancelAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, cancellationToken)
            .ConfigureAwait(false);

        if (appointment is null)
            return Result<CancelAppointmentResponse>.Fail(
                new Error("Appointment.NotFound", $"Appointment {command.AppointmentId} not found."));

        var result = appointment.Cancel(command.Reason, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return Result<CancelAppointmentResponse>.Fail(result.Error!);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await mediator.Publish(
            new AppointmentCancelledIntegrationEvent(
                appointment.Id,
                appointment.PatientId,
                tenantContext.TenantId,
                appointment.CancelledAt!.Value,
                timeProvider.GetUtcNow()),
            cancellationToken)
            .ConfigureAwait(false);

        return Result<CancelAppointmentResponse>.Ok(
            new CancelAppointmentResponse(appointment.Id, appointment.Status.ToString()));
    }
}
```

The endpoint: `POST /appointments/{id}/cancel` with body `{ "reason": "Patient request" }`.

### `OnAppointmentCancelledHandler`

```csharp
public sealed class OnAppointmentCancelledHandler(
    IDbContextFactory<NotificationsDbContext> dbFactory,
    IBackgroundJobClient backgroundJobs)
    : INotificationHandler<AppointmentCancelledIntegrationEvent>
{
    public async ValueTask Handle(
        AppointmentCancelledIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await db.Notifications
            .SingleOrDefaultAsync(n =>
                n.AppointmentId == notification.AppointmentId &&
                n.TemplateKey == TemplateKeys.AppointmentReminder &&
                n.Status == NotificationStatus.Scheduled,
                cancellationToken)
            .ConfigureAwait(false);

        // No scheduled notification: consent was denied, patient wasn't found, etc.
        if (record is null)
            return;

        if (record.HangfireJobId is not null)
            backgroundJobs.Delete(record.HangfireJobId);

        record.MarkCancelled();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

This runs in the same HTTP request as the cancel command — the tenant context is live, so no `BackgroundJobTenantScope` is needed here. The handler finds the notification by appointment ID and `Scheduled` status only. If the reminder was already sent (the appointment was cancelled with less than 24 hours to go), there's no `Scheduled` record to find and the handler returns cleanly.

`backgroundJobs.Delete(jobId)` returns `false` if the job already ran — the double-cancel scenario (two cancellation requests arrive simultaneously). The `MarkCancelled()` call still runs, which is correct: the notification's final state reflects the appointment's final state.

---

## The full flow end-to-end

```
Patient books appointment (3 weeks out)
  → BookAppointmentHandler saves appointment
  → Publishes AppointmentBookedIntegrationEvent
  → OnAppointmentBookedHandler schedules Hangfire job (fires at: appointment.ScheduledAt - 24h)
  → Writes Notification { Status = Scheduled, HangfireJobId = "abc123" }
  → Response: 200 OK

                    ... 20 days pass ...

Patient calls to cancel
  → CancelAppointmentHandler saves Cancelled status
  → Publishes AppointmentCancelledIntegrationEvent
  → OnAppointmentCancelledHandler finds Notification { Status = Scheduled }
  → Calls backgroundJobs.Delete("abc123") — job removed from Hangfire queue
  → Marks Notification { Status = Cancelled, HangfireJobId = null }
  → Response: 200 OK

                    ... no SMS sent ...

OR: patient keeps the appointment

                    ... appointment minus 24 hours ...

  → Hangfire worker thread picks up job "abc123"
  → AppointmentReminderJob.SendAsync fires
  → BackgroundJobTenantScope.Current = clinicId  ← tenant context restored
  → Reads Notification { Status = Scheduled }  ← not yet sent or cancelled
  → GetPatientContactQuery via Mediator
  → Consent check passes
  → INotificationSender.SendAsync → SMS delivered
  → Marks Notification { Status = Sent, SentAt = now, HangfireJobId = null }
```

---

## What about `TimeProvider`?

The system uses `TimeProvider.GetUtcNow()` everywhere instead of `DateTime.UtcNow`. This means `reminderAt = notification.ScheduledAt - ReminderLeadTime` is computed with the same clock as the rest of the system. In tests, you can inject a `FakeTimeProvider` and control exactly when "now" is — which makes it possible to test the "< 24 hours" enqueue branch without actually waiting.

---

## Hangfire dashboard

In development, `https://localhost:5001/hangfire` shows the live job queue:

- **Enqueued**: jobs waiting for a worker
- **Scheduled**: jobs with a future fire time
- **Succeeded**: completed jobs with their arguments
- **Failed**: jobs that threw exceptions, with retry counts and stack traces

This is genuinely useful during development. When a `POST /appointments` returns 200 and you see no SMS log, the first thing to check is whether the Scheduled job is sitting in the queue or failed. The arguments panel shows the serialized `appointmentId`, `patientId`, `clinicId`, and `scheduledAt` — which tells you immediately whether the event was published correctly.

In production, the dashboard should be gated. Hangfire provides `DashboardOptions.Authorization` for this:

```csharp
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAdminRoleFilter()]
});
```

Where `HangfireAdminRoleFilter` implements `IDashboardAuthorizationFilter` and checks for the Admin role claim.

---

## What this design does not do

**Retry exhaustion.** By default Hangfire retries a failed job 10 times with exponential backoff. After 10 retries it moves to the `Failed` state and stops. The notification record will say `Failed`. A real system would want an alert when a job exhausts its retries — a webhook or a metric on the failed queue depth.

**Distributed lock for the idempotency check.** The idempotency check (`AnyAsync` before `Add`) has a narrow race: two concurrent events for the same appointment could both pass the check before either writes. In practice, `AppointmentBookedIntegrationEvent` is published once per booking by a single handler, so duplicates only arrive if the event bus delivers at-least-once and you have a queue-based setup. For in-process Mediator, this race cannot occur. If you ever move to an out-of-process broker (RabbitMQ, Azure Service Bus), add a unique index on `(TenantId, AppointmentId, TemplateKey)` in the notifications table.

**Outbox pattern.** The Hangfire job is scheduled and the notification is written in the same request, but there is no distributed transaction between them. If the `SaveChangesAsync` call fails after `backgroundJobs.Schedule` succeeds, you have a Hangfire job with no corresponding notification record. The idempotency check in the job handles the case where the record is missing (`if (record is null) return`), so the job will fire and do nothing — no SMS sent, no error. A production-grade solution would use the outbox pattern to make the job scheduling and the DB write atomic. For a single-doctor practice, this edge case is acceptable.

---

## What changed

| Layer | Change |
|---|---|
| `Core` | `BackgroundJobTenantScope` — `AsyncLocal<Guid>` for background tenant context |
| `MedClinic.Api` | `HttpTenantContext` checks background scope first; Hangfire wired in `Program.cs`; dashboard at `/hangfire` |
| `Notifications` | `NotificationStatus` + `Cancelled`; `Notification.HangfireJobId`; `AppointmentReminderJob`; `OnAppointmentBookedHandler` schedules instead of sends; `OnAppointmentCancelledHandler` cancels job |
| `Appointments` | `CancelAppointmentCommand/Handler/Validator/Endpoint`; `AppointmentCancelledIntegrationEvent` |
| Migrations | `AddNotificationHangfireJobId` — nullable `text` column on `notifications.notifications` |

---

## What's next

The clinic now has:
- Patients with consent tracking
- Appointments with check-in and cancellation
- Clinical encounters with diagnoses and vitals
- Prescriptions tied to encounters
- JWT auth with role enforcement
- Invoices that generate from encounters
- Consent-gated SMS reminders that fire 24 hours before appointments
- A test suite enforcing module boundaries and tenant isolation

Part 12 will look at **observability**: structured logging with Serilog, health checks, and the metrics a real clinic would want to watch — appointment no-shows, failed SMS delivery rates, and failed login attempts. The groundwork for the last of these is already laid: every `LoginHandler` result is logged (without the password, of course), and every failed reminder is marked `Failed` in the notifications table. Part 12 is about surfacing that data.

---

*The code for this article is in the [MediClinic repository](https://github.com) on branch `master`, commit tagged `article/part-11`. All seven modules, the test suite, the AI agent layer, and the Hangfire infrastructure are there.*
