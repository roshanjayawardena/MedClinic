# Module: Encounters

Owns the clinical consultation — the doctor's record of what happened during a visit. This is the
most PHI-sensitive module in the system. Read `phi-and-tenancy.md` and `auditing.md` before touching anything here.

---

## Responsibility

| In scope | Out of scope |
|---|---|
| Clinical notes (SOAP or free-text) | Booking/scheduling (Appointments) |
| Diagnoses (ICD-10 codes + description) | Drug dispensing (Prescriptions) |
| Vital signs (BP, HR, temp, weight, SpO₂) | Patient demographics (Patients) |
| Encounter lifecycle (Open → Closed) | Billing (triggered by `EncounterClosedIntegrationEvent`) |

---

## Encounter aggregate — lifecycle

```
Open → Closed
```

- An Encounter is created from a **completed Appointment** (`AppointmentId` is required; validates existence via Contracts).
- `Close()` locks the record — no edits after closing. A closed Encounter triggers prescription creation.
- Clinical notes, diagnoses, and vitals can only be set/updated while `Status == Open`.
- The doctor (role check) is the only user who can create or close an Encounter.

---

## Audit requirement (non-negotiable)

**Every read and every write** of an Encounter must emit a `ClinicalRecordAudited` event via the Outbox:

```csharp
// On GET /encounters/{id}
auditOutbox.Enqueue(new ClinicalRecordAudited(
    ActorId: actor.Id,
    Action: AuditAction.Read,
    EntityType: nameof(Encounter),
    EntityId: encounter.Id,
    ClinicId: tenant.ClinicId,
    OccurredAt: timeProvider.GetUtcNow()));
```

No audit event = a `phi-review` workflow failure. This is enforced before merge.

---

## Layout

```
src/Modules/Encounters/
├── Encounters/
│   ├── EncountersModule.cs
│   ├── Domain/
│   │   ├── Encounter.cs
│   │   ├── EncounterStatus.cs
│   │   ├── Diagnosis.cs           (owned entity — not a separate DbSet)
│   │   └── VitalSigns.cs          (value object — stored as owned)
│   ├── Persistence/
│   │   ├── EncountersDbContext.cs
│   │   └── Configurations/
│   │       └── EncounterConfiguration.cs
│   └── Features/
│       ├── OpenEncounter/
│       ├── RecordVitals/
│       ├── AddDiagnosis/
│       ├── UpdateNotes/
│       ├── CloseEncounter/
│       ├── GetEncounter/          (AUDITED read)
│       └── GetEncounterSummary/   (AUDITED read — lighter DTO for Prescriptions)
└── Encounters.Contracts/
    ├── OpenEncounter.cs
    ├── CloseEncounter.cs
    ├── Events/
    │   └── EncounterClosedIntegrationEvent.cs
    └── EncounterExistsQuery.cs    (Prescriptions validates this)
```

---

## PHI mapping — what fields are PHI

| Field | PHI? | Safe to log? |
|---|---|---|
| `Encounter.Id` | No | Yes — surrogate GUID |
| `Encounter.PatientId` | No | Yes — surrogate GUID |
| `Encounter.Notes` | **YES** | Never |
| `Diagnosis.IcdCode` | **YES** | Never (discloses condition) |
| `Diagnosis.Description` | **YES** | Never |
| `VitalSigns.*` (BP, HR…) | **YES** | Never |

---

## Module-specific gotchas

- **No Encounter without an Appointment.** Validate `AppointmentExistsQuery` and that the appointment
  is in `Completed` status before `OpenEncounter` succeeds.
- **Closed = immutable.** Any write attempt on a closed Encounter returns `Result.Fail(...)`.
- **Pharmacist cannot create Encounters.** Enforce the `Encounters.Create` permission (Doctor role only).
  The pharmacist can read summaries via `GetEncounterSummary` (requires `Encounters.ReadSummary`).
- **Diagnoses are ICD-10.** Validate the code format (`[A-Z]\d{2}(\.\d{1,4})?`) in the validator.
