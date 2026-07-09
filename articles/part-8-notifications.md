# Part 8: Consent-Gated Reminders — Sending Appointment Notifications the Right Way

*Building a MediClinic SaaS — Part 8 of an ongoing series*

---

Every patient communication system has the same failure mode: a developer wires up event → send SMS, ships it, and later discovers they've been texting patients who explicitly opted out of communications. In a medical context, that's not just bad UX — it's a regulatory breach.

This article builds the Notifications module and makes the correct pattern impossible to bypass:

1. **`ConsentToCommunications` before every outreach** — the check is structural, not procedural
2. **Notifications as a pure consumer** — it subscribes to events from other modules, exposes no API, holds no business data
3. **`INotificationSender` abstraction** — swap Twilio for SendGrid (or a console stub for development) by changing one DI registration
4. **PHI discipline in sender code** — the phone number never appears in a log line
5. **Two concrete subscribers** — `AppointmentBooked` → reminder, `InvoicePaid` → payment confirmation

---

## Why Notifications has no Contracts project

Every module in this series has a `.Contracts` project that exposes commands, queries, and integration events for other modules to use. Notifications has none.

A `.Contracts` project exists to give other modules something to reference — a query to send, an event to subscribe to. Notifications is a pure consumer: it receives events from Appointments and Billing and writes a delivery record. Nothing else in the system needs to query Notifications, and Notifications publishes no events of its own.

The rule: **a Contracts project exists only when other modules need it.** If a module only listens, the Contracts project is dead weight.

---

## The consent check is structural, not a checklist item

The wrong pattern is:

```csharp
// DON'T do this — the consent check is buried and easy to forget
public async Task Handle(AppointmentBookedIntegrationEvent evt, ...)
{
    await sender.SendAsync(patient.PhoneNumber, message);  // forgot consent check
}
```

The right pattern makes consent the first thing after fetching contact data:

```csharp
var contact = await mediator.Send(new GetPatientContactQuery(notification.PatientId), cancellationToken);

if (!contact.Value.ConsentToCommunications)
{
    // Record the attempt — not sending is a business decision, not an error.
    db.Notifications.Add(Notification.Record(..., NotificationStatus.ConsentDenied));
    await db.SaveChangesAsync(cancellationToken);
    return;  // ← early return; sender is never called
}
```

Returning early means the `INotificationSender` call is structurally unreachable when consent is denied. There is no code path from "consent not given" to "message sent".

### Why record `ConsentDenied` at all?

Because "no message was sent" is a fact worth auditing. The `notifications.notifications` table now contains:

| TemplateKey | Status | PatientId |
|---|---|---|
| AppointmentReminder | ConsentDenied | patient-A-id |
| AppointmentReminder | Sent | patient-B-id |

When a patient calls saying they didn't receive a reminder, you can answer: "We have a record that you have communications consent off. Here's the timestamp when we checked." That's an audit trail, not a missing feature.

---

## GetPatientContactQuery: minimal data across module boundaries

The Notifications module needs only two fields from the Patients module: the phone number and the consent flag. To enforce this, a new query was added to `Patients.Contracts`:

```csharp
// Patients.Contracts/GetPatientContact.cs
/// <summary>
/// Minimal cross-module query — returns only the fields needed for outreach decisions.
/// Does NOT return PHI fields (name, DOB) that callers do not need.
/// </summary>
public sealed record GetPatientContactQuery(Guid PatientId)
    : IRequest<Result<GetPatientContactResponse>>;

/// <summary>
/// ContactPhone is PHI. Callers must use it immediately and never log it.
/// </summary>
public sealed record GetPatientContactResponse(
    Guid PatientId,
    string ContactPhone,
    bool ConsentToCommunications);
```

Compare this to the existing `GetPatientByIdQuery`, which also returns `FirstName`, `LastName`, and `DateOfBirth`. Those three fields are PHI that the Notifications module has no reason to hold. Defining a separate query enforces the **principle of minimal access**: a caller only receives what it asked for and what it has a legitimate reason to receive.

If a developer on the Notifications module later wants to personalise the SMS ("Hi Sarah, your appointment is..."), they have to explicitly add `FirstName` to `GetPatientContactResponse` — a visible, reviewable change. Under the existing `GetPatientByIdQuery`, they could access the name silently.

---

## The sender abstraction

```csharp
public sealed record NotificationMessage(
    NotificationChannel Channel,
    string Recipient,  // PHI — never log; use immediately and discard
    string Body);

public interface INotificationSender
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}
```

The development stub writes to the console without logging the recipient:

```csharp
public sealed class ConsoleNotificationSender(ILogger<ConsoleNotificationSender> logger)
    : INotificationSender
{
    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        // IMPORTANT: Never log message.Recipient — it is PHI (patient phone number).
        logger.LogInformation(
            "[NOTIFICATION STUB] Channel={Channel} Body length={Length} chars. (recipient suppressed — PHI)",
            message.Channel,
            message.Body.Length);

        return Task.CompletedTask;
    }
}
```

The log line contains no recipient. The `message.Body` length is logged (useful for debugging truncation), but not the body itself (it may contain the appointment time, which combined with the number is identifying).

To swap in Twilio in production, register a different implementation:

```csharp
// In NotificationsModule.RegisterServices:
services.AddScoped<INotificationSender, ConsoleNotificationSender>();  // dev
// services.AddScoped<INotificationSender, TwilioNotificationSender>(); // production
```

The handlers never change. The provider decision lives in one line of DI wiring.

### Error handling in the sender

```csharp
try
{
    await sender.SendAsync(
        new NotificationMessage(NotificationChannel.Sms, contact.ContactPhone, body),
        cancellationToken).ConfigureAwait(false);
    status = NotificationStatus.Sent;
    sentAt = now;
}
catch (Exception ex)
{
    // Log only the exception type — the message may contain the phone number.
    logger.LogError("SMS send failed: {ExceptionType}", ex.GetType().Name);
    status = NotificationStatus.Failed;
    failureReason = ex.GetType().Name;
}
```

The `ex.Message` is deliberately not logged. Provider SDK exceptions often include the phone number in the message string (`"Invalid number: +64 21 555 1234"`). Logging `ex.GetType().Name` gives enough signal for alerting without leaking PHI.

---

## The Notification entity

`Notification` extends `AuditableEntity` — it gets automatic `TenantId` stamping, soft-delete, and `CreatedAt` from `BaseDbContext`. The entity records what happened, not who the patient is beyond their ID:

```csharp
public sealed class Notification : AuditableEntity
{
    public Guid PatientId { get; private set; }
    public Guid? AppointmentId { get; private set; }     // null for invoice notifications
    public NotificationChannel Channel { get; private set; }
    public NotificationStatus Status { get; private set; }  // Sent, Failed, ConsentDenied
    public string TemplateKey { get; private set; }          // "AppointmentReminder", etc.
    public DateTimeOffset? SentAt { get; private set; }
    public string? FailureReason { get; private set; }       // exception type only, never PHI
}
```

No phone number. No message body. No patient name. The notification record is an audit entry — it answers "did we attempt to notify this patient about this event, and what happened?" — without storing PHI beyond what was already stored elsewhere in the system.

---

## The two event handlers

### `AppointmentBooked` → appointment reminder

```
AppointmentBookedIntegrationEvent fires
    ↓
Idempotency check: has a reminder for this appointment already been sent?
    ↓ no
GetPatientContactQuery → Patients module returns (phone, consentFlag)
    ↓
consentFlag == false → record ConsentDenied, return
    ↓
Build SMS body: "Reminder: your appointment is on Friday 11 July 2025 at 2:30 PM..."
    ↓
INotificationSender.SendAsync(phone, body)
    ↓
Record Notification with status Sent (or Failed if sender threw)
```

### `InvoicePaid` → payment confirmation

```
InvoicePaidIntegrationEvent fires (from Part 7 Billing module)
    ↓
GetPatientContactQuery → consent check
    ↓
Build SMS body: "Payment of $150.00 received via Card. Thank you for visiting."
    ↓
INotificationSender.SendAsync → record Notification
```

Notice what's in the payment confirmation body: the amount and payment method. These come from the integration event payload, not from PHI. No patient name, no diagnosis, no drug name. The message is clinically neutral.

---

## Idempotency

Both handlers check for an existing notification before doing any work:

```csharp
var alreadyHandled = await db.Notifications
    .AnyAsync(n => n.AppointmentId == notification.AppointmentId
                && n.TemplateKey == TemplateKeys.AppointmentReminder, cancellationToken)
    .ConfigureAwait(false);

if (alreadyHandled) return;
```

In an in-process Mediator system, this guard is defensive. The day the event bus moves to a message broker (RabbitMQ, Azure Service Bus), at-least-once delivery guarantees mean this check becomes essential. Writing the guard now costs nothing; removing it later after discovering duplicate SMS messages costs considerably more.

---

## Why notifications fires synchronously (and why that's fine for now)

The event handler runs synchronously in the same HTTP request that triggered the event. When a receptionist books an appointment, the HTTP response doesn't return until the SMS stub has run.

In a production system, you'd push notification jobs onto a background queue (Hangfire, a channel, a message broker) so the HTTP response returns immediately and the SMS delivery is eventually consistent. The interface `INotificationSender` already supports this: a `QueuedNotificationSender` implementation could enqueue the job instead of calling Twilio directly.

The article series keeps it synchronous to focus on the consent and PHI patterns, not on background job infrastructure. The architecture supports the upgrade.

---

## The `NotificationsModule`

```csharp
[assembly: MedClinicModule(typeof(NotificationsModule), order: 70)]

public sealed class NotificationsModule : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<NotificationsDbContext>(...);

        // Single line to swap the provider:
        services.AddScoped<INotificationSender, ConsoleNotificationSender>();
    }

    // Pure consumer — no HTTP endpoints.
    public void MapEndpoints(IEndpointRouteBuilder app) { }
}
```

`MapEndpoints` is intentionally empty. Notifications doesn't expose an API surface. A receptionist doesn't call `POST /notifications` — notifications happen automatically as a side-effect of clinical events.

If an admin needs to view notification history, that would be a `GET /notifications/patients/{id}` endpoint added in a future iteration, using the same handler pattern as every other read in this system.

---

## Running the migration

```bash
dotnet ef migrations add InitialNotificationsCreate \
  --context NotificationsDbContext \
  --project src/Host/MedClinic.Migrations.PostgreSQL \
  --startup-project src/Host/MedClinic.Migrations.PostgreSQL \
  --output-dir Migrations/Notifications

dotnet run --project src/Host/MedClinic.DbMigrator
```

The migration creates `notifications.notifications` — one row per notification attempt.

To verify the full flow end-to-end:

```bash
# 1. Register a patient with consent
POST /patients
{ "consentToCommunications": true, ... }

# 2. Book an appointment
POST /appointments
→ AppointmentBookedIntegrationEvent fires
→ OnAppointmentBookedHandler runs synchronously
→ Console log: "[NOTIFICATION STUB] Channel=Sms Body length=87 chars."
→ notifications.notifications row: status=Sent

# 3. Register without consent
POST /patients
{ "consentToCommunications": false, ... }
# Book an appointment for this patient
→ notifications.notifications row: status=ConsentDenied
```

---

## What we built

| Artifact | Purpose |
|---|---|
| `GetPatientContactQuery` in `Patients.Contracts` | Minimal cross-module query: phone + consent only |
| `GetPatientContactHandler` in `Patients` | Returns contact fields; never returns PHI beyond what's requested |
| `Notification : AuditableEntity` | Delivery record: channel, status, template, sent timestamp |
| `NotificationStatus` | Sent, Failed, ConsentDenied |
| `TemplateKeys` | Constants: AppointmentReminder, PaymentConfirmation |
| `INotificationSender` | Provider abstraction: one interface, swappable implementations |
| `ConsoleNotificationSender` | Development stub — logs body length only, never recipient |
| `OnAppointmentBookedHandler` | Subscribes to `AppointmentBookedIntegrationEvent`; consent-gated SMS |
| `OnInvoicePaidHandler` | Subscribes to `InvoicePaidIntegrationEvent`; consent-gated SMS |
| `NotificationsModule` | No endpoints — pure consumer registration |
| `InitialNotificationsCreate` migration | `notifications.notifications` table |

---

## What's next

That completes the core clinical modules: Patients, Appointments, Encounters, Prescriptions, Identity, Billing, and Notifications. The system now has:

- A complete patient-to-payment flow driven by domain events
- Tenant isolation enforced at every layer
- PHI protection throughout: no logging of names, DOBs, drug names, or phone numbers
- Consent gating on all outbound communications
- A swappable provider model for every external service

Part 9 will cover **integration testing with Testcontainers** — spinning up a real PostgreSQL instance per test suite, verifying the event-driven flows end-to-end, and writing architecture tests with NetArchTest that enforce the module boundary rules at build time.
