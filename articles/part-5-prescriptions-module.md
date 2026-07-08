# Part 5: The Pharmacist's Workflow — Drug Orders, Allergy Checks, and Dispensing

*Building a MedClinic SaaS — Part 5 of an ongoing series*

---

In the previous article we built the Encounters module and established the PHI audit trail. Now we tackle the Prescriptions module — the pharmacist's domain.

This part covers five things that separate a production-grade system from a tutorial project:

1. **Business rule enforcement across modules** — no prescription without a closed encounter
2. **Cross-module data without coupling** — allergy checks via Contracts-only queries
3. **Drug names as PHI** — why a common logging habit becomes a compliance violation
4. **Aggregate state machines** — Draft → Active → Dispensed with hard invariant enforcement
5. **Integration events for downstream billing** — `PrescriptionDispensedIntegrationEvent`

The complete source is in the `article/part-5` tag of the repository.

---

## The domain rules

Before writing a line of code, let's be precise about what the business requires:

| Rule | Where enforced |
|---|---|
| A prescription requires a **closed** encounter | `WritePrescriptionHandler` checks `ClosedEncounterRecord` |
| Pharmacist can dispense; only doctor can write | Role boundary (enforced in Identity, Part 6) |
| Drug name matches a recorded patient allergy → conflict | Cross-module allergy query in `WritePrescriptionHandler` |
| `Dispensed` prescriptions cannot be cancelled | Aggregate state machine |
| Every Prescription read or write emits an audit entry | Golden rule 9 |
| Drug names must not appear in logs | Logging PHI rule |

---

## Extending the Patients module: allergies

The allergy data lives in Patients — that module is responsible for everything we know about a patient's medical history. The Prescriptions module will query it through `Patients.Contracts`.

### The Allergy entity

Extending `AuditableEntity` gives us tenant isolation and soft-delete for free:

```csharp
// Patients/Domain/Allergy.cs
public sealed class Allergy : AuditableEntity
{
    private Allergy() { }

    public Guid PatientId { get; private set; }
    public string DrugName { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public string? Notes { get; private set; }

    public static Allergy Record(Guid patientId, string drugName, string severity, string? notes) =>
        new()
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            DrugName = drugName,
            Severity = severity,
            Notes = notes,
        };
}
```

`BaseDbContext` automatically applies the `TenantId` filter to every `AuditableEntity` subtype, so no special query filter is needed here.

### Cross-module query contract

The allergy query lives in `Patients.Contracts` so that any module can use it without taking a runtime dependency on the Patients module itself:

```csharp
// Patients.Contracts/GetPatientAllergies.cs
public sealed record GetPatientAllergiesQuery(Guid PatientId)
    : IRequest<Result<GetPatientAllergiesResponse>>;

public sealed record GetPatientAllergiesResponse(IReadOnlyList<PatientAllergyDto> Allergies);

public sealed record PatientAllergyDto(string DrugName, string Severity);
```

The handler in `Patients` queries the `Allergies` DbSet and projects to the DTO. No PHI in the DTO beyond the drug name — which is itself PHI. This is why the response is only ever consumed programmatically for comparison, never logged.

---

## Why drug names are PHI

You might not instinctively think of a drug name as protected health information, but under HIPAA and most healthcare data regulations, a drug name **combined with a patient identifier** reveals a diagnosis. "Lisinopril for patient X" tells you patient X has hypertension or heart failure.

The violation usually looks like this:

```csharp
// VIOLATION — this goes into your log aggregator in plaintext
_logger.LogInformation(
    "Prescription written for {PatientId}: {DrugName} {Dosage}",
    command.PatientId, command.DrugName, command.DosageInstructions);
```

This lands in Elastic/Splunk/Datadog, searchable by anyone with log access. The compliant version:

```csharp
// CORRECT — surrogate IDs only in logs
_logger.LogInformation("Prescription {PrescriptionId} written", prescription.Id);
```

The same rule applies to the `PrescriptionDispensedIntegrationEvent`. We publish the event after dispense so Billing can create an invoice — but the drug name is intentionally excluded:

```csharp
public sealed record PrescriptionDispensedIntegrationEvent(
    Guid PrescriptionId,
    Guid EncounterId,
    Guid PatientId,
    Guid ClinicId,
    DateTimeOffset DispensedAt,
    DateTimeOffset OccurredAt) : INotification;
// DrugName excluded intentionally — it is PHI and must not transit integration events.
```

Billing doesn't need the drug name to create a consultation fee. If it ever did, it should query `GetPrescriptionById` directly — and that access would itself emit an audit entry.

---

## The `ClosedEncounterRecord` pattern

The Prescriptions module must verify that an encounter is closed before accepting a prescription. But the Prescriptions module cannot query the Encounters `DbContext` — that's a cross-module runtime dependency, which violates the golden rules.

The solution: **subscribe to `EncounterClosedIntegrationEvent` and maintain a local read model.**

```csharp
// Prescriptions/Domain/ClosedEncounterRecord.cs
public sealed class ClosedEncounterRecord
{
    public Guid EncounterId { get; init; }
    public Guid PatientId { get; init; }
    public Guid TenantId { get; init; }    // manual tenant field
    public DateTimeOffset ClosedAt { get; init; }
}
```

This is a **projection**, not a domain entity. It doesn't extend `AuditableEntity` because it has no lifecycle — it's append-only data derived from an external event. We configure its query filter manually in `PrescriptionsDbContext`.

The subscriber:

```csharp
// Features/OnEncounterClosed/OnEncounterClosedHandler.cs
public sealed class OnEncounterClosedHandler(IDbContextFactory<PrescriptionsDbContext> dbFactory)
    : INotificationHandler<EncounterClosedIntegrationEvent>
{
    public async ValueTask Handle(
        EncounterClosedIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Idempotency guard — in-process events can be delivered once, but
        // real message brokers (Part 9) guarantee at-least-once delivery.
        var alreadyRecorded = await db.ClosedEncounters
            .AnyAsync(r => r.EncounterId == notification.EncounterId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyRecorded) return;

        db.ClosedEncounters.Add(new ClosedEncounterRecord
        {
            EncounterId = notification.EncounterId,
            PatientId = notification.PatientId,
            TenantId = notification.ClinicId,
            ClosedAt = notification.OccurredAt,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

The idempotency guard matters even with in-process pub/sub. We'll replace this with a real message broker in a later part — and that broker will guarantee *at-least-once* delivery, meaning duplicates are possible. Building idempotency now means zero refactoring when we make that switch.

---

## The Prescription aggregate

### State machine

```
                      ┌──────────────────────────────┐
                      │          Cancelled           │
                      └──────────────────────────────┘
                              ▲           ▲
                              │ Cancel()  │ Cancel()
                              │           │
Write() ──► Draft ─── Activate() ──► Active ─── Dispense() ──► Dispensed
```

The aggregate enforces these transitions:

```csharp
public Result Activate(DateTimeOffset now)
{
    if (Status != PrescriptionStatus.Draft)
        return Result.Fail(new Error("Prescription.InvalidStatus",
            $"Only a Draft prescription can be activated. Current status: {Status}."));

    Status = PrescriptionStatus.Active;
    ActivatedAt = now;
    return Result.Ok();
}

public Result Dispense(DateTimeOffset now)
{
    if (Status != PrescriptionStatus.Active)
        return Result.Fail(new Error("Prescription.InvalidStatus",
            $"Only an Active prescription can be dispensed. Current status: {Status}."));

    Status = PrescriptionStatus.Dispensed;
    DispensedAt = now;
    return Result.Ok();
}

public Result Cancel(string reason, DateTimeOffset now)
{
    if (Status is PrescriptionStatus.Dispensed or PrescriptionStatus.Cancelled)
        return Result.Fail(new Error("Prescription.InvalidStatus",
            $"Cannot cancel a prescription with status {Status}."));

    Status = PrescriptionStatus.Cancelled;
    CancellationReason = reason;
    CancelledAt = now;
    return Result.Ok();
}
```

Notice the comment in the `DrugName` field:

```csharp
// DrugName is PHI — never logged. Stored encrypted in production; plaintext here for reference simplicity.
public string DrugName { get; private set; } = string.Empty;
```

In a real production system the drug name column would be encrypted at rest (column-level encryption or application-level AES-256). For this reference implementation, we call out the intention without the implementation complexity.

---

## WritePrescription: two business rules in sequence

The `WritePrescriptionHandler` is the most interesting handler in this module because it enforces two cross-module invariants:

```csharp
public async ValueTask<Result<WritePrescriptionResponse>> Handle(
    WritePrescriptionCommand command,
    CancellationToken cancellationToken)
{
    await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

    // Rule 1: prescription requires a closed encounter.
    var encounterClosed = await db.ClosedEncounters
        .AnyAsync(r => r.EncounterId == command.EncounterId
                    && r.PatientId == command.PatientId, cancellationToken)
        .ConfigureAwait(false);

    if (!encounterClosed)
        return Result<WritePrescriptionResponse>.Fail(new Error(
            "Prescription.NoClosedEncounter",
            $"Encounter {command.EncounterId} is not closed or does not belong to patient {command.PatientId}."));

    // Rule 2: allergy conflict check via cross-module query.
    var allergiesResult = await mediator
        .Send(new GetPatientAllergiesQuery(command.PatientId), cancellationToken)
        .ConfigureAwait(false);

    if (!allergiesResult.IsSuccess)
        return Result<WritePrescriptionResponse>.Fail(allergiesResult.Error!);

    var conflict = allergiesResult.Value.Allergies
        .FirstOrDefault(a =>
            a.DrugName.Contains(command.DrugName, StringComparison.OrdinalIgnoreCase)
            || command.DrugName.Contains(a.DrugName, StringComparison.OrdinalIgnoreCase));

    if (conflict is not null)
        return Result<WritePrescriptionResponse>.Fail(new Error(
            "Prescription.AllergyConflict",
            $"Patient has a recorded {conflict.Severity} allergy that conflicts with the prescribed drug."));

    var prescription = Prescription.Write(
        command.EncounterId,
        command.PatientId,
        command.DrugName,
        command.DosageInstructions,
        command.QuantityDays);

    db.Prescriptions.Add(prescription);
    db.AuditEntries.Add(new AuditEntry(
        Guid.NewGuid(),
        tenantContext.TenantId,
        Action: "PrescriptionWritten",
        EntityType: nameof(Prescription),
        EntityId: prescription.Id.ToString(),
        PerformedBy: null,
        timeProvider.GetUtcNow()));

    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

    return Result<WritePrescriptionResponse>.Ok(new WritePrescriptionResponse(prescription.Id));
}
```

Two things worth noting:

**Rule 1 uses the local read model**, not a cross-module query. The `ClosedEncounterRecord` is populated by `OnEncounterClosedHandler`. This keeps the handler fast (one DB call against the local schema) and decoupled (no dependency on the Encounters runtime).

**Rule 2 uses a cross-module Mediator query**. The `GetPatientAllergiesQuery` is dispatched through the same in-process Mediator that handles everything else. The handler in `Patients` runs, returns the allergy list, and the Prescriptions handler checks for a match. No HTTP calls, no shared database tables, no shared types beyond what's in `Patients.Contracts`.

The allergy matching uses a bidirectional contains check: if the recorded allergy is "Penicillin" and the prescribed drug is "Amoxicillin-Penicillin", that's a conflict. And vice versa. A real clinical system would use a drug interaction database, but this demonstrates the pattern correctly.

---

## The dispense handler and the integration event

When a pharmacist dispenses a prescription, we:

1. Load the aggregate
2. Call `prescription.Dispense(now)` — the state machine validates the transition
3. Emit an audit entry in the same `SaveChanges`
4. **After the commit**, publish `PrescriptionDispensedIntegrationEvent`

```csharp
await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

// Publish after commit — Billing subscribes to create an invoice.
await mediator.Publish(
    new PrescriptionDispensedIntegrationEvent(
        prescription.Id,
        prescription.EncounterId,
        prescription.PatientId,
        tenantContext.TenantId,
        prescription.DispensedAt!.Value,
        timeProvider.GetUtcNow()),
    cancellationToken).ConfigureAwait(false);
```

The publish happens after `SaveChanges` for the same reason as in the Encounters module: we don't want subscribers reacting to an event if the dispense record failed to persist.

The Billing module (Part 7) will subscribe to this event and create a consultation invoice. Until then, the Mediator source generator emits a `MSG0005` warning — "no registered handler" — which is expected and harmless.

---

## PrescriptionsDbContext: two manual query filters

`PrescriptionsDbContext` manages three entity types with different tenant filter strategies:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasDefaultSchema("prescriptions");

    modelBuilder.Entity<Prescription>(p =>
    {
        p.ToTable("prescriptions");
        p.Property(x => x.Status).HasConversion<string>().HasMaxLength(50);
        // ... column config
    });

    // ClosedEncounterRecord is not AuditableEntity — filter manually.
    modelBuilder.Entity<ClosedEncounterRecord>(r =>
    {
        r.ToTable("closed_encounter_records");
        r.HasKey(x => x.EncounterId);
        r.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
    });

    // AuditEntry is append-only — filter manually.
    modelBuilder.Entity<AuditEntry>(audit =>
    {
        audit.ToTable("audit_entries");
        audit.HasQueryFilter(a => a.TenantId == tenantContext.TenantId);
        // ...
    });

    // BaseDbContext applies tenant + soft-delete filter to Prescription automatically.
    base.OnModelCreating(modelBuilder);
}
```

`Prescription` inherits `AuditableEntity` so `BaseDbContext` handles its filter. `ClosedEncounterRecord` and `AuditEntry` are not `AuditableEntity` subtypes, so they need explicit `HasQueryFilter` calls. Without these, a query on `ClosedEncounters` would return records from all clinics.

---

## The complete API surface

```
POST   /patients/{patientId}/allergies          AddAllergy
GET    /patients/{patientId}/allergies          GetPatientAllergies
POST   /prescriptions                           WritePrescription
POST   /prescriptions/{id}/activate             ActivatePrescription
POST   /prescriptions/{id}/dispense             DispensePrescription
GET    /prescriptions/{id}                      GetPrescriptionById (audit-logged)
```

Note that `GetPrescriptionById` returns `DosageInstructions` and `QuantityDays` but **not** `DrugName`. This keeps PHI out of casual API responses. If a UI needs to display the drug name, it should be behind an additional authorization layer that generates its own audit entry.

---

## Running the migrations

Two migrations this time — one for the new allergies table in the Patients schema, one for the new Prescriptions schema:

```bash
# Allergies table in the patients schema
dotnet ef migrations add AddAllergiesTable \
  --context PatientsDbContext \
  --project src/Host/MedClinic.Migrations.PostgreSQL \
  --startup-project src/Host/MedClinic.Migrations.PostgreSQL \
  --output-dir Migrations/Patients

# Prescriptions initial schema
dotnet ef migrations add InitialPrescriptionsCreate \
  --context PrescriptionsDbContext \
  --project src/Host/MedClinic.Migrations.PostgreSQL \
  --startup-project src/Host/MedClinic.Migrations.PostgreSQL \
  --output-dir Migrations/Prescriptions

# Apply both
dotnet run --project src/Host/MedClinic.DbMigrator
```

---

## What we built

| Artifact | Purpose |
|---|---|
| `Allergy` entity + `AddAllergy`/`GetPatientAllergies` | Extended Patients module with drug allergy data |
| `GetPatientAllergiesQuery` in Patients.Contracts | Cross-module allergy query used by Prescriptions |
| `ClosedEncounterRecord` | Local read model — encounter closed projection |
| `OnEncounterClosedHandler` | Subscribes to `EncounterClosedIntegrationEvent`, persists projection |
| `Prescription` aggregate | Draft → Active → Dispensed state machine |
| `WritePrescriptionHandler` | Enforces closed-encounter + allergy-conflict rules |
| `ActivatePrescriptionHandler` | Draft → Active transition with audit |
| `DispensePrescriptionHandler` | Active → Dispensed transition, publishes `PrescriptionDispensedIntegrationEvent` |
| `GetPrescriptionByIdHandler` | Read with mandatory audit entry |
| `PrescriptionDispensedIntegrationEvent` | Signals Billing (Part 7) to create an invoice |

---

## Patterns and principles summary

**The read model pattern** (`ClosedEncounterRecord`) lets one module make decisions that depend on another module's state without querying across boundaries at runtime. The data arrives via integration events and is stored locally. The trade-off: the data is eventually consistent. In practice, the Mediator pub/sub is synchronous in our current setup, so the record is available before the HTTP response returns.

**Allergy checking via Contracts** demonstrates how cross-module queries work. The Prescriptions module declares a dependency on `Patients.Contracts` in its `.csproj` — never on the Patients runtime project. The Mediator source generator in the host sees both the query (in Contracts) and the handler (in Patients) and wires them up automatically.

**PHI in integration events** — the rule is simple: integration events are broadcast to any subscriber, potentially persisted in a message broker, and visible in logs. Drug names never appear in them.

**State machines via `Result<T>`** — the aggregate methods return `Result` rather than throwing. The handler propagates the failure directly:

```csharp
var result = prescription.Dispense(timeProvider.GetUtcNow());
if (!result.IsSuccess)
    return Result<DispensePrescriptionResponse>.Fail(result.Error!);
```

No exceptions, no catches, no surprises.

---

## What's next

Part 6 covers Identity — JWT authentication, roles (Doctor, Pharmacist, Receptionist, Admin), and permission-based authorization. We'll wire in the pharmacist constraint that prevents them from writing prescriptions or opening encounters, and the doctor constraint that prevents them from dispensing.

The `PerformedBy` field on every `AuditEntry` is currently `null`. After Part 6, it will carry the authenticated user's ID — completing the audit trail.
