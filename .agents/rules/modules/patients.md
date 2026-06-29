# Module: Patients

The reference module — the layout here is the pattern every other module follows.
Read `architecture.md`, `database.md`, and `phi-and-tenancy.md` before working here.

## Responsibility

Owns patient demographics, contact details, consent, and allergies for a clinic. Does **not** own
appointments, encounters, or prescriptions — those are separate modules that reference a patient by Id
through this module's Contracts.

## Layout (canonical reference)

```
src/Modules/Patients/
├── Patients/                          # runtime
│   ├── PatientsModule.cs              # IModule: RegisterServices + MapEndpoints
│   ├── Domain/
│   │   └── Patient.cs                 # aggregate: private setters, behavior methods
│   ├── Persistence/
│   │   ├── PatientsDbContext.cs       # : BaseDbContext (tenant filter ON)
│   │   └── Configurations/PatientConfiguration.cs
│   └── Features/
│       └── RegisterPatient/
│           ├── RegisterPatientHandler.cs
│           ├── RegisterPatientValidator.cs
│           └── RegisterPatientEndpoint.cs
└── Patients.Contracts/
    ├── RegisterPatient.cs             # Command + Response records
    └── PatientExistsQuery.cs          # cross-module read surface (see below)
```

## The Patient aggregate

- Inherits `AuditableEntity` (tenant key, soft-delete, audit fields).
- PHI fields: `FirstName`, `LastName`, `DateOfBirth`, `ContactPhone`, address. **Never logged in plaintext.**
- Consent flags: `ConsentToDataProcessing` (required at registration), `ConsentToCommunications`
  (gates reminders/outreach — see `phi-and-tenancy.md`).
- `Allergies` (used later by Prescriptions for conflict checks).
- Mutating properties are `private set`; changes go through intent methods.

## Features

| Feature | Type | Notes |
|---|---|---|
| `RegisterPatient` | command | requires `ConsentToDataProcessing`; emits a domain event on creation |

(Add `GetPatients`, `UpdatePatientContact`, `RecordConsent`, etc. as later parts need them.)

## Cross-module surface (Contracts only)

Other modules must not touch the Patients runtime. They ask through Contracts:

- `PatientExistsQuery(Guid PatientId)` → bool, scoped to the current clinic. Used by Appointments to
  validate a booking target. Keep EF types out of Contracts — the query returns primitives/DTOs only.

## Module-specific gotchas

- **Consent is not optional at registration.** The validator must reject a registration without
  `ConsentToDataProcessing`.
- **Soft-delete only.** Deactivating a patient flags + audits; never hard-delete.
- **No PHI leaves the module unredacted.** A cross-module response may carry the patient Id and minimal
  display fields the caller is permitted to see — never the full clinical/PHI set.
