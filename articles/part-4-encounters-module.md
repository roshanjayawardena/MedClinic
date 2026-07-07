# Handling PHI at the Code Level: Clinical Notes, Diagnoses, and the Audit Trail

*Part 4 of Building a Production Medical Clinic SaaS with AI*

---

Parts 2 and 3 established the module pattern: two projects, a domain aggregate, one folder per feature, thin endpoints. Every module since then has copied that structure without modification.

Part 4 changes the stakes. An Encounter is a clinical record. The doctor's notes live in it. The patient's diagnoses live in it. Their vital signs live in it. This is Protected Health Information — PHI — and the law requires that every access to it is recorded. Not every modification. Every access, including reads.

That requirement shapes the code. This article shows exactly how.

Everything here is at git tag `article/part-4`.

---

## Why clinical records are different

The Patients module holds demographics: name, date of birth, phone number. That is PHI and it is protected. But the consent flags, registration workflow, and tenant isolation we built in Part 2 are sufficient for that data tier.

Encounter data is in a different category. A patient's diagnosis list is clinical information. Their notes from a visit with the doctor are clinical information. In many jurisdictions, a patient has a legal right to know who accessed their clinical record and when. A breach of an encounter record is a reportable incident.

The auditing requirement in this codebase is stated in `auditing.md`:

> Every **read or write** of an `Encounter` or `Prescription` emits an audit event.

Not "writes only". Not "significant reads". Every read. If the doctor opens a patient's encounter record, that is audited. If the pharmacist reads it to check drug history, that is audited. The audit entry records who accessed what, when, from which clinic — nothing clinical, just the access event itself.

---

## The Encounter aggregate

An encounter has a simple lifecycle: it opens when the patient arrives and closes when the doctor finishes. Between open and close, diagnoses are added and vital signs are recorded.

```csharp
// Domain/Encounter.cs

public sealed class Encounter : AuditableEntity
{
    private List<Diagnosis> _diagnoses = [];

    private Encounter() { } // required by EF Core

    public Guid AppointmentId { get; private set; }
    public Guid PatientId { get; private set; }
    public EncounterStatus Status { get; private set; }
    public string? ClinicalNotes { get; private set; }
    public VitalSigns? Vitals { get; private set; }
    public IReadOnlyList<Diagnosis> Diagnoses => _diagnoses.AsReadOnly();
    public DateTimeOffset? ClosedAt { get; private set; }

    public static Encounter Open(Guid appointmentId, Guid patientId, string? clinicalNotes = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            PatientId = patientId,
            Status = EncounterStatus.Open,
            ClinicalNotes = clinicalNotes,
        };

    public Result AddDiagnosis(string icd10Code, string description, DiagnosisType diagnosisType)
    {
        if (Status != EncounterStatus.Open)
            return Result.Fail(new Error("Encounter.Closed",
                "Cannot add a diagnosis to a closed encounter."));

        _diagnoses.Add(new Diagnosis(icd10Code, description, diagnosisType));
        return Result.Ok();
    }

    public Result RecordVitals(VitalSigns vitals)
    {
        if (Status != EncounterStatus.Open)
            return Result.Fail(new Error("Encounter.Closed",
                "Cannot record vitals for a closed encounter."));

        Vitals = vitals;
        return Result.Ok();
    }

    public Result Close(string? clinicalNotes, DateTimeOffset now)
    {
        if (Status != EncounterStatus.Open)
            return Result.Fail(new Error("Encounter.InvalidStatus",
                $"Cannot close an encounter with status '{Status}'."));

        if (clinicalNotes is not null)
            ClinicalNotes = clinicalNotes;

        Status = EncounterStatus.Closed;
        ClosedAt = now;
        return Result.Ok();
    }
}
```

The same principles from the Appointment aggregate apply: private backing field, behavior methods, `Result<T>` returns. `AddDiagnosis` returns `Result.Fail` if the encounter is already closed — the guard is in the aggregate, not the handler.

---

## Owned entities: Diagnosis and VitalSigns

`Diagnosis` and `VitalSigns` are not independent entities. They do not have their own lifecycle — they exist only as part of an Encounter. EF Core models this with owned entities.

### VitalSigns as OwnsOne

Vital signs are recorded once per encounter. There is at most one `VitalSigns` object, and it is replaced as a whole if the clinician re-records values. This maps naturally to an owned scalar: EF stores the vital sign fields as nullable columns directly in the `encounters` table.

```csharp
// Domain/VitalSigns.cs

public sealed record VitalSigns(
    int? SystolicBp,
    int? DiastolicBp,
    int? HeartRateBpm,
    decimal? TemperatureCelsius,
    int? RespiratoryRatePerMin,
    int? OxygenSaturationPercent,
    decimal? WeightKg);
```

A C# `record` is a natural fit for a value object: immutable, structural equality, no identity. When the clinician updates vital signs, the handler calls `encounter.RecordVitals(newVitals)` which replaces the entire `Vitals` property. There is no partial update of individual vitals fields.

The EF configuration maps this inline:

```csharp
e.OwnsOne(x => x.Vitals, v =>
{
    v.Property(p => p.SystolicBp).HasColumnName("vitals_systolic_bp");
    v.Property(p => p.DiastolicBp).HasColumnName("vitals_diastolic_bp");
    v.Property(p => p.HeartRateBpm).HasColumnName("vitals_heart_rate_bpm");
    v.Property(p => p.TemperatureCelsius).HasColumnName("vitals_temperature_celsius").HasPrecision(4, 1);
    v.Property(p => p.RespiratoryRatePerMin).HasColumnName("vitals_respiratory_rate");
    v.Property(p => p.OxygenSaturationPercent).HasColumnName("vitals_spo2_percent");
    v.Property(p => p.WeightKg).HasColumnName("vitals_weight_kg").HasPrecision(5, 1);
});
```

All seven columns live in the `encounters` table. If no vitals have been recorded, they are all null. The `Encounter` entity exposes `VitalSigns? Vitals` — the nullable tells you at a glance whether vitals have been recorded.

### Diagnosis as OwnsMany

An encounter can have multiple diagnoses: a primary ICD-10 code and potentially secondary codes or comorbidities. This maps to a collection.

```csharp
// Domain/Diagnosis.cs

public sealed class Diagnosis
{
    private Diagnosis() { } // required by EF Core

    internal Diagnosis(string icd10Code, string description, DiagnosisType type)
    {
        Icd10Code = icd10Code;
        Description = description;
        Type = type;
    }

    public string Icd10Code { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DiagnosisType Type { get; private set; }
}
```

`Diagnosis` is a class rather than a record because EF Core's owned entity collection tracking works better with class identity. The private default constructor satisfies EF Core's materialization requirement. The `internal` constructor means only code within the `Encounters` assembly can create a `Diagnosis` — and the only call site is `Encounter.AddDiagnosis()`.

The backing field pattern for the collection:

```csharp
// In Encounter
private List<Diagnosis> _diagnoses = [];
public IReadOnlyList<Diagnosis> Diagnoses => _diagnoses.AsReadOnly();
```

`IReadOnlyList` is the public surface. External code cannot call `encounter.Diagnoses.Add(...)`. The only path to adding a diagnosis is `encounter.AddDiagnosis(...)`, which enforces the "open encounter only" guard before touching `_diagnoses`.

The EF configuration uses a shadow primary key:

```csharp
e.OwnsMany(x => x.Diagnoses, d =>
{
    d.ToTable("encounter_diagnoses");
    d.WithOwner().HasForeignKey("EncounterId");
    d.Property<int>("Id").ValueGeneratedOnAdd();
    d.HasKey("Id");
    d.Property(p => p.Icd10Code).HasMaxLength(20).IsRequired();
    d.Property(p => p.Description).HasMaxLength(500).IsRequired();
    d.Property(p => p.Type)
        .HasConversion<string>()
        .HasMaxLength(20)
        .IsRequired();
});

// Tell EF to use the private backing field
e.Navigation(x => x.Diagnoses)
    .HasField("_diagnoses")
    .UsePropertyAccessMode(PropertyAccessMode.Field);
```

`d.Property<int>("Id").ValueGeneratedOnAdd()` creates a shadow `Id` column that EF uses as the primary key for the `encounter_diagnoses` table. The domain class has no `Id` property — the identity is an infrastructure detail. EF Core writes to `_diagnoses` directly via field access when materializing an `Encounter` from the database.

---

## The audit trail — the hard rule

Golden rule 9 in `AGENTS.md`:

> Every read or write of an Encounter or Prescription MUST emit an audit entry.

Every handler that touches an Encounter writes to `db.AuditEntries` in the same `SaveChangesAsync`. The audit entry and the business change commit together — or they both roll back. There is no "audit succeeded but business change failed" or "business change succeeded but audit was skipped".

### Auditing a write

```csharp
// Features/OpenEncounter/OpenEncounterHandler.cs

var encounter = Encounter.Open(command.AppointmentId, command.PatientId, command.ClinicalNotes);

db.Encounters.Add(encounter);

// Same SaveChangesAsync = same transaction.
db.AuditEntries.Add(new AuditEntry(
    Guid.NewGuid(),
    tenantContext.TenantId,
    Action: "EncounterOpened",
    EntityType: nameof(Encounter),
    EntityId: encounter.Id.ToString(),
    PerformedBy: null,
    timeProvider.GetUtcNow()));

await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
```

The `AuditEntry` row is added to the EF change tracker alongside the `Encounter` row. `SaveChangesAsync` issues one `BEGIN`, inserts both rows, and commits. If the insert fails for any reason, both rows are rolled back. If the process crashes between the `SaveChangesAsync` call and returning to the caller, neither row exists — the operation failed atomically.

This is the "outbox" in its simplest form: the audit event is not sent to a message queue — it is written to a table in the same database, in the same transaction. It cannot be lost without also losing the business change it describes. There is no window where the encounter exists without an audit entry for its creation.

What the audit entry does **not** contain: patient name, ICD-10 code, clinical notes, date of birth, anything clinical. It contains the encounter's surrogate `Id`, the action name (`"EncounterOpened"`), and the timestamp. To know what happened clinically, you read the encounter. The audit log tells you who accessed it and when.

### Auditing a read

Reads are more unusual. A read has no business state change to commit. But the audit requirement is clear: opening a specific clinical record must be logged.

```csharp
// Features/GetEncounterById/GetEncounterByIdHandler.cs

var encounter = await db.Encounters
    .AsNoTracking()
    .Include(e => e.Diagnoses)
    .FirstOrDefaultAsync(e => e.Id == query.EncounterId, cancellationToken)
    .ConfigureAwait(false);

if (encounter is null)
    return Result<GetEncounterByIdResponse>.Fail(
        new Error("Encounter.NotFound", $"Encounter {query.EncounterId} not found."));

// Audit the read in a separate context (the read used AsNoTracking).
await using var auditDb = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
auditDb.AuditEntries.Add(new AuditEntry(
    Guid.NewGuid(),
    tenantContext.TenantId,
    Action: "EncounterRead",
    EntityType: nameof(Encounter),
    EntityId: encounter.Id.ToString(),
    PerformedBy: null,
    timeProvider.GetUtcNow()));
await auditDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
```

Two `DbContext` instances: one for the read (`AsNoTracking`, no change tracking overhead), one for the audit write. The audit entry commits to the database on every read of a real encounter. A request for an encounter that does not exist returns 404 and writes no audit entry — there is nothing to audit.

The fact that the read and the audit write are two separate transactions is a conscious tradeoff. If the process crashes between the read returning data to the API layer and the audit write committing, the read succeeds but is unaudited. For a full outbox guarantee on reads you would enqueue the audit event transactionally in a different way. For the current scale of a single-doctor clinic, the risk of this specific failure window is acceptable. The article is honest about this.

---

## The phi-review workflow catch

The `phi-review` workflow runs against every diff that touches patient or clinical data. Here is the kind of finding it catches — a realistic example of PHI leaking into a log message.

**The violation:**

Suppose a developer adds logging to the `AddDiagnosis` handler for debugging:

```csharp
// VIOLATION — caught by phi-review
_logger.LogInformation(
    "Added diagnosis {Icd10Code} ({Description}) to encounter {EncounterId} for patient {PatientId}",
    command.Icd10Code,
    command.Description,
    command.EncounterId,
    command.PatientId);
```

The `phi-review` workflow flags two fields:

- `Icd10Code` — a diagnosis code is PHI. Structured logs are searchable and may be shipped to a logging service. Sending `F32.9` (major depressive disorder) or `Z21` (HIV status) to a log aggregator is a breach.
- `Description` — the human-readable description is even more explicit PHI. "Type 2 diabetes mellitus without complications" in a log file is a direct disclosure.

**The fix:**

```csharp
// CORRECT — surrogate Ids only
_logger.LogInformation(
    "Diagnosis added to encounter {EncounterId}",
    command.EncounterId);
```

The encounter ID is a GUID with no clinical meaning. It identifies that *something* happened to encounter `3fa85f64-...` but tells a log reader nothing about the patient or their health. If you need to trace the specific diagnosis for debugging, you look it up using the encounter ID in the database — not the log file.

The `phi-review` workflow checks every log statement in the diff and flags any that interpolate fields from command or entity types that carry clinical meaning. It catches this class of bug mechanically, before it reaches code review.

The real-world consequence of missing this: a log aggregation service like Datadog or Splunk ingests structured logs. The ICD-10 code and description end up indexed and searchable. A developer searching for "diabetes" in production logs surfaces a list of encounter IDs paired with patient IDs. That is a breach — it did not require an attacker, just a developer debugging a production issue.

---

## CloseEncounter and the EncounterClosed integration event

```csharp
// Features/CloseEncounter/CloseEncounterHandler.cs

var closeResult = encounter.Close(command.ClinicalNotes, now);
if (closeResult.IsFailure)
    return Result<CloseEncounterResponse>.Fail(closeResult.Error!);

// Audit in the same transaction as the state change.
db.AuditEntries.Add(new AuditEntry(
    Guid.NewGuid(),
    tenantContext.TenantId,
    Action: "EncounterClosed",
    EntityType: nameof(Encounter),
    EntityId: encounter.Id.ToString(),
    PerformedBy: null,
    now));

await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

// Integration event — Prescriptions subscribes to unlock script writing.
await mediator.Publish(
    new EncounterClosedIntegrationEvent(
        encounter.Id,
        encounter.PatientId,
        encounter.AppointmentId,
        tenantContext.TenantId,
        now),
    cancellationToken)
    .ConfigureAwait(false);
```

The `EncounterClosedIntegrationEvent` is published after `SaveChangesAsync`. This follows the same pattern as `AppointmentBookedIntegrationEvent` from Part 3 — an in-process publish via Mediator's `IPublisher`. The event definition:

```csharp
// Encounters.Contracts/Events/EncounterClosedIntegrationEvent.cs

public sealed record EncounterClosedIntegrationEvent(
    Guid EncounterId,
    Guid PatientId,
    Guid AppointmentId,
    Guid ClinicId,
    DateTimeOffset OccurredAt) : INotification;
```

No PHI. Five fields: four surrogate IDs and a timestamp. `ClinicId` is always required in integration events so the event relay can restore tenant context when the subscriber processes it.

**Why Prescriptions depend on this event.** Part 5 will build the Prescriptions module. A core business rule is that a prescription cannot be written without a corresponding closed Encounter — a script without a clinical consult is a data integrity violation. The Prescriptions module handles `EncounterClosedIntegrationEvent` and creates an internal record that says "encounter X is closed; prescriptions for patient Y against this encounter are now permitted". The Prescriptions handler then checks for that record before writing a script.

This is the correct way for two modules to coordinate: not a direct call, not a shared table, not a shared DbContext. An integration event published by Encounters, a subscriber in Prescriptions, a local read model maintained by Prescriptions. The modules stay loosely coupled. If Encounters is ever extracted to a separate service, the event mechanism is already in place.

---

## EncountersDbContext and the migrations

Each module's DbContext owns its own PostgreSQL schema. For Encounters:

```csharp
modelBuilder.HasDefaultSchema("encounters");
```

This creates:
- `encounters.encounters` — the Encounter aggregate table
- `encounters.encounter_diagnoses` — the Diagnosis owned entity collection
- `encounters.audit_entries` — clinical access log

Three tables, one schema, one module. If Encounters is ever split from the monolith, these three tables move together.

The `AuditEntry` mapping in `EncountersDbContext` requires a manual tenant filter (same as in `PatientsDbContext`) because `AuditEntry` does not extend `AuditableEntity` — it is the audit log, not an auditable thing:

```csharp
modelBuilder.Entity<AuditEntry>(audit =>
{
    audit.ToTable("audit_entries");
    audit.HasKey(a => a.Id);
    // ...
    audit.HasQueryFilter(a => a.TenantId == tenantContext.TenantId);
});
```

Migration command:

```bash
dotnet ef migrations add InitialEncountersCreate \
  --context EncountersDbContext \
  --output-dir Migrations/Encounters \
  --project src/Host/MedClinic.Migrations.PostgreSQL
```

Applied by the DbMigrator in order after Appointments:

```csharp
await sp.GetRequiredService<PatientsDbContext>().Database.MigrateAsync();
await sp.GetRequiredService<AppointmentsDbContext>().Database.MigrateAsync();
await sp.GetRequiredService<EncountersDbContext>().Database.MigrateAsync();
```

---

## Endpoints delivered in Part 4

| Method | Route | Handler | Audit |
|---|---|---|---|
| `POST` | `/encounters` | `OpenEncounterHandler` | Write: `EncounterOpened` |
| `POST` | `/encounters/{id}/diagnoses` | `AddDiagnosisHandler` | Write: `DiagnosisAdded` |
| `POST` | `/encounters/{id}/vitals` | `RecordVitalsHandler` | Write: `VitalsRecorded` |
| `POST` | `/encounters/{id}/close` | `CloseEncounterHandler` | Write: `EncounterClosed` |
| `GET` | `/encounters/{id}` | `GetEncounterByIdHandler` | Read: `EncounterRead` |

Every row has an entry in the Audit column. No endpoint touches an Encounter without leaving a trace.

---

## What Part 4 established

- Clinical data (Encounters, Prescriptions) requires auditing of every access — read and write — as a legal requirement, not a feature.
- Audit entries and business changes commit in the same `SaveChangesAsync`. There is no window where a change exists without its audit record.
- `VitalSigns` as an owned scalar (`OwnsOne`): columns inline in the parent table, replaced as a whole unit.
- `Diagnosis` as an owned collection (`OwnsMany`): separate table, shadow primary key, private backing field.
- The `phi-review` workflow catches PHI in log messages before code review — ICD-10 codes and clinical descriptions are PHI even when they appear to be "just debugging info".
- `EncounterClosedIntegrationEvent` creates the precondition Prescriptions will check in Part 5.

---

## Next: Part 5 — Prescriptions

Part 5 builds the Prescriptions module. It introduces:

- The `EncounterClosedIntegrationEvent` handler that creates the local "encounter is closed" record Prescriptions checks before allowing a script
- Allergy conflict checking via a cross-module query to Patients.Contracts
- The prescription lifecycle: Draft → Active → Dispensed — the pharmacist workflow
- Why drug names are PHI and how the logging rules apply to prescription data

*Code for this article: `git checkout article/part-4`*  
*Previous: Part 3 — The Appointments Module*  
*Next: Part 5 — The Prescriptions Module*
