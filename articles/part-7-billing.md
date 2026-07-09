# Part 7: Simple Billing for a Private Clinic — Invoices Triggered by Clinical Events

*Building a MediClinic SaaS — Part 7 of an ongoing series*

---

Billing is the module that closes the clinical loop: a doctor sees a patient, the encounter is closed, and an invoice appears — automatically. No receptionist manually creates a billing record. No chance of the consultation going unbilled because someone forgot.

This article builds the Billing module and demonstrates the full event-driven flow:

1. **Why Billing is its own module** — not bolted onto Appointments or Encounters
2. **`EncounterClosed` → Draft invoice** — event subscription and idempotent projection
3. **Invoice lifecycle** — Draft → Issued → Paid, and the Void escape hatch
4. **Line items and procedure codes** — simple enough to be real, extensible enough to grow
5. **`InvoicePaidIntegrationEvent`** — closing the loop for future modules

---

## Why billing is its own module

The temptation is to put billing inside Appointments: after all, the appointment is what the patient books, and the consultation fee is why they come. Or inside Encounters: the clinical record is what triggers the charge.

Neither is right.

**Separation of concerns.** An appointment can be cancelled before it starts — no billable event. An encounter can be opened but not closed — still no billable event. Billing logic needs to respond to the *completion* of clinical work, not to scheduling or documentation. That completion signal is `EncounterClosedIntegrationEvent`.

**Different lifecycle.** An encounter's lifecycle is clinical: Open → Closed. An invoice's lifecycle is financial: Draft → Issued → Paid. They share a foreign key but their state machines are entirely independent.

**Future flexibility.** A clinic might add procedure codes, discounts, insurance integration, payment processors, or GST handling. None of that belongs in `EncountersDbContext`. Billing owns its own schema, its own migrations, and its own domain.

**The module boundary enforces this.** Billing references only `Encounters.Contracts` (to subscribe to the event) — never the Encounters runtime project. It cannot accidentally start reading `EncountersDbContext` tables.

---

## The event-driven trigger

When a doctor closes an encounter:

1. `CloseEncounterHandler` saves the Encounter entity with `Status = Closed`.
2. It publishes `EncounterClosedIntegrationEvent` *after* `SaveChangesAsync()` — ensuring the data is committed before the event fires.
3. `OnEncounterClosedHandler` in Billing receives the event and creates a Draft invoice.

The Billing handler:

```csharp
public sealed class OnEncounterClosedHandler(IDbContextFactory<BillingDbContext> dbFactory)
    : INotificationHandler<EncounterClosedIntegrationEvent>
{
    private const decimal ConsultationFee = 150.00m;

    public async ValueTask Handle(
        EncounterClosedIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Idempotency guard — one invoice per encounter.
        var alreadyExists = await db.Invoices
            .AnyAsync(i => i.EncounterId == notification.EncounterId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyExists) return;

        var item = InvoiceLineItem.Create(
            description: "Consultation fee",
            procedureCode: null,
            unitPrice: ConsultationFee,
            quantity: 1);

        var invoice = Invoice.Create(
            notification.PatientId,
            notification.EncounterId,
            notification.AppointmentId,
            [item]);

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

Two things worth calling out:

**The idempotency guard.** In-process Mediator delivers events exactly once, but the day this system moves to a message broker (RabbitMQ, Azure Service Bus), at-least-once delivery is the norm. The guard ensures a second delivery of the same `EncounterClosedIntegrationEvent` is a no-op. The database unique index on `(TenantId, EncounterId)` is a second layer of defence.

**The `AppointmentId` on the invoice.** The event carries it. The invoice stores it. Now finance can trace: Invoice → Encounter → Appointment → Patient. The foreign key chain is complete without a cross-module join.

---

## Why `EncounterClosed`, not `AppointmentCompleted`

The spec for this article mentioned `AppointmentCompleted → draft invoice`. In the MediClinic model, `EncounterClosedIntegrationEvent` is the correct trigger:

| Event | When it fires | Billable? |
|---|---|---|
| `AppointmentBooked` | Patient schedules | No — work not done |
| `AppointmentCompleted` | Appointment status changes | Ambiguous — appointment may or may not have an encounter |
| `EncounterClosed` | Doctor finishes clinical documentation | Yes — clinical work is done |

A walk-in patient may skip the appointment booking entirely and go straight to an encounter. A booked appointment may be marked complete without any clinical documentation. Only a closed encounter — with diagnoses, vitals, and notes — guarantees that billable clinical work occurred. That's the trigger.

---

## The Invoice domain entity

`Invoice` inherits from `AuditableEntity`, giving it automatic `TenantId` stamping, soft-delete, and `CreatedAt`/`ModifiedAt` tracking from `BaseDbContext`. The state machine is the same pattern as `Encounter` and `Prescription` — private setters, factory method, `Result`-returning transition methods:

```csharp
public sealed class Invoice : AuditableEntity
{
    private List<InvoiceLineItem> _lineItems = [];
    private Invoice() { }

    public Guid PatientId { get; private set; }
    public Guid EncounterId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public IReadOnlyList<InvoiceLineItem> LineItems => _lineItems.AsReadOnly();
    public DateTimeOffset? IssuedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? PaymentMethod { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    public static Invoice Create(Guid patientId, Guid encounterId, Guid appointmentId,
        IEnumerable<InvoiceLineItem> lineItems)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            EncounterId = encounterId,
            AppointmentId = appointmentId,
            Status = InvoiceStatus.Draft,
        };
        invoice._lineItems.AddRange(lineItems);
        invoice.TotalAmount = invoice._lineItems.Sum(l => l.LineTotal);
        return invoice;
    }

    public Result Issue(DateTimeOffset now) { /* Draft → Issued */ }
    public Result RecordPayment(string paymentMethod, DateTimeOffset now) { /* Issued → Paid */ }
    public Result Void(string reason, DateTimeOffset now) { /* Draft|Issued → Void; Paid cannot be voided */ }
}
```

The state machine enforced by these methods:

```
        Issue()          RecordPayment()
Draft ─────────► Issued ────────────────► Paid
  │                 │
  │    Void()       │    Void()
  └────────────►  Void  ◄────────────────┘
                                    (Paid cannot be voided)
```

The `Void` guard — `if (Status == InvoiceStatus.Paid) return Result.Fail(...)` — is the business rule that prevents reversing a completed financial transaction. An overpayment or billing error requires a credit note, not a void, in real clinical billing.

---

## Line items: kept simple, built to extend

`InvoiceLineItem` is an owned EF entity with three fields:

```csharp
public sealed class InvoiceLineItem
{
    public string Description { get; private set; }
    public string? ProcedureCode { get; private set; }  // CPT/ICD procedure code
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal LineTotal => UnitPrice * Quantity;   // computed in C#, not stored
}
```

`ProcedureCode` is optional for now. CPT codes (in the US) or SNOMED procedure codes (elsewhere) can be mapped here when insurance billing is added. The column exists in the schema — nullable — so no migration is needed when the feature lands.

`LineTotal` is computed in C# (`UnitPrice * Quantity`) and explicitly ignored in EF:

```csharp
li.Ignore(p => p.LineTotal);
```

The alternative is a database computed column, but that ties the formula to the migration. C#-computed keeps it close to the domain and testable without a database.

`TotalAmount` on the Invoice *is* stored — it's denormalised at creation time and never recalculated. Line items don't change after the invoice is created. If they did, `TotalAmount` would need an `UpdateTotal()` method and a fresh `SaveChanges`.

---

## The BillingDbContext

`BillingDbContext` follows the same pattern as every other module: inherits `BaseDbContext<T>`, calls `base.OnModelCreating()` last, uses `HasDefaultSchema("billing")`.

The unique index on `(TenantId, EncounterId)` is the database enforcement of the one-invoice-per-encounter rule:

```csharp
e.HasIndex(x => new { x.TenantId, x.EncounterId }).IsUnique();
```

This is a second line of defence after the idempotency guard in the event handler. Even if the in-memory guard is bypassed (race condition on two concurrent event deliveries), the database constraint wins.

The owned `LineItems` collection uses the field-backed navigation pattern to keep EF out of the private `_lineItems` field:

```csharp
e.Navigation(x => x.LineItems)
    .HasField("_lineItems")
    .UsePropertyAccessMode(PropertyAccessMode.Field);
```

---

## The invoice lifecycle in HTTP terms

| Endpoint | Transition | Who calls it |
|---|---|---|
| `POST /invoices/{id}/issue` | Draft → Issued | Receptionist or system |
| `POST /invoices/{id}/pay` | Issued → Paid | Receptionist after collecting payment |
| `POST /invoices/{id}/void` | Draft/Issued → Void | Admin with a reason |
| `GET /invoices/{id}` | — | Any authenticated user with billing read |

The `IssueInvoice` endpoint takes no body — the invoice ID is enough:

```csharp
private static async Task<IResult> Handle(Guid id, IMediator mediator, CancellationToken ct)
{
    var result = await mediator.Send(new IssueInvoiceCommand(id), ct);
    return result.IsSuccess
        ? TypedResults.Ok(result.Value)
        : TypedResults.ValidationProblem(...);
}
```

The `RecordPayment` endpoint takes a `PaymentMethod` body:

```csharp
internal sealed record RecordPaymentRequest(string PaymentMethod);

private static async Task<IResult> Handle(Guid id, RecordPaymentRequest body, IMediator mediator, CancellationToken ct)
{
    var result = await mediator.Send(new RecordPaymentCommand(id, body.PaymentMethod), ct);
    ...
}
```

`PaymentMethod` is a free-text string for now: `"Cash"`, `"Card"`, `"BankTransfer"`. A production system would use an enum or a reference table.

---

## `InvoicePaidIntegrationEvent`

When payment is recorded, the `RecordPaymentHandler` publishes an integration event after `SaveChangesAsync()`:

```csharp
await mediator.Publish(
    new InvoicePaidIntegrationEvent(
        invoice.Id,
        invoice.PatientId,
        invoice.EncounterId,
        tenantContext.TenantId,
        invoice.TotalAmount,
        invoice.PaymentMethod!,
        invoice.PaidAt!.Value,
        now),
    cancellationToken).ConfigureAwait(false);
```

No module currently subscribes to this event, but it's published for Part 8 (Notifications) — a payment confirmation SMS or email would be triggered here.

Notice `TotalAmount` is included in the event payload. Is that PHI? No — financial amounts are not protected health information. Including the amount lets downstream modules avoid a cross-module query for the invoice total.

---

## The end-to-end billing flow

```
1. Doctor closes encounter
   CloseEncounterHandler → SaveChanges → publishes EncounterClosedIntegrationEvent

2. Billing receives the event
   OnEncounterClosedHandler → idempotency check → Invoice.Create(consultationFee) → SaveChanges
   Invoice status: Draft

3. Receptionist issues the invoice
   POST /invoices/{id}/issue
   IssueInvoiceHandler → invoice.Issue(now) → SaveChanges
   Invoice status: Issued

4. Patient pays
   POST /invoices/{id}/pay { paymentMethod: "Card" }
   RecordPaymentHandler → invoice.RecordPayment("Card", now) → SaveChanges
                        → publishes InvoicePaidIntegrationEvent
   Invoice status: Paid
```

The doctor never touches the billing screen. The pharmacist doesn't see the invoice. The receptionist works in the billing view. Role boundaries hold across the financial workflow.

---

## Running the migration

```bash
dotnet ef migrations add InitialBillingCreate \
  --context BillingDbContext \
  --project src/Host/MedClinic.Migrations.PostgreSQL \
  --startup-project src/Host/MedClinic.Migrations.PostgreSQL \
  --output-dir Migrations/Billing

dotnet run --project src/Host/MedClinic.DbMigrator
```

The migration creates:
- `billing.invoices` — one row per clinical encounter (unique constraint on `TenantId + EncounterId`)
- `billing.invoice_line_items` — owned table, one row per service rendered

To test the full flow:

```bash
# 1. Open and close an encounter (see Part 4)
POST /encounters  →  { encounterId: "abc..." }
POST /encounters/abc/close

# 2. Find the auto-created invoice (the event handler ran synchronously)
GET /invoices/{invoice-id}
# → { status: "Draft", totalAmount: 150.00, lineItems: [{ description: "Consultation fee" }] }

# 3. Issue it
POST /invoices/{id}/issue
# → { issuedAt: "..." }

# 4. Record payment
POST /invoices/{id}/pay
{ "paymentMethod": "Card" }
# → { paidAt: "..." }
```

---

## What we built

| Artifact | Purpose |
|---|---|
| `Invoice : AuditableEntity` | Domain entity with Draft→Issued→Paid state machine |
| `InvoiceLineItem` | Owned entity: description, procedure code, unit price, quantity |
| `InvoiceStatus` | Enum: Draft, Issued, Paid, Void |
| `BillingDbContext` | EF context, `billing` schema, unique index on `(TenantId, EncounterId)` |
| `OnEncounterClosedHandler` | Subscribes to `EncounterClosedIntegrationEvent`, creates Draft invoice |
| `CreateInvoiceHandler` | Manual invoice creation for edge cases |
| `IssueInvoiceHandler` | Draft → Issued transition |
| `RecordPaymentHandler` | Issued → Paid; publishes `InvoicePaidIntegrationEvent` |
| `VoidInvoiceHandler` | Draft/Issued → Void |
| `GetInvoiceByIdHandler` | Tenant-scoped invoice read |
| `InvoicePaidIntegrationEvent` | Published after payment; Notifications subscribes in Part 8 |
| `InitialBillingCreate` migration | `billing.invoices` + `billing.invoice_line_items` tables |

---

## What's next

Part 8 covers **Notifications** — the module that subscribes to `InvoicePaidIntegrationEvent` and sends a payment confirmation, and to `AppointmentBookedIntegrationEvent` for reminder SMS/email. It also revisits the `ConsentToCommunications` field from Part 1: no message goes out without explicit patient opt-in.

We'll also look at how to test event-driven modules in isolation with Testcontainers — verifying that the event handler fires, the database row appears, and the downstream event is published, all without mocking.
