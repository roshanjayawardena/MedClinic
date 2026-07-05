# Logging — structured logs, PHI safety, Serilog conventions

Read this before adding any log statement anywhere in the codebase.

## Always use structured logging

Use Serilog message templates with named properties — **never** string interpolation.

```csharp
// WRONG — loses structure, can leak PHI
_logger.LogInformation($"Patient {patient.Name} registered.");

// CORRECT — structured, PHI-safe (Id only, no name)
_logger.LogInformation("Patient registered. PatientId={PatientId} ClinicId={ClinicId}",
    patient.Id, tenant.ClinicId);
```

## PHI is absolutely prohibited in logs

Treat these as PHI — **never** include them in any log message, exception, or trace attribute:
- Patient name (first, last, or combined)
- Date of birth
- Phone number, email address, home address
- Diagnosis, medication name, clinical note content
- National ID / passport / health card number

Surrogate identifiers (GUIDs) are safe to log. Derived aggregates (age band, appointment count) are safe.
Raw PHI values are not — even in development or debug-level logs.

```csharp
// WRONG
_logger.LogWarning("Encounter {EncounterId} for {PatientName} has no diagnosis.", id, patient.FullName);

// CORRECT
_logger.LogWarning("Encounter {EncounterId} closed without diagnosis. PatientId={PatientId}",
    encounterId, patientId);
```

## Log levels — use the right level

| Level | When to use |
|---|---|
| `Trace` | Step-by-step execution trace — development only, never in production |
| `Debug` | Values and state useful for diagnosing; not routinely collected in production |
| `Information` | Normal business operations: patient registered, appointment booked, invoice created |
| `Warning` | Unexpected but recoverable: retry occurred, soft business rule violated, config fallback |
| `Error` | Unhandled exception or operation failure; requires attention |
| `Critical` | System is unable to continue; data integrity risk |

## Enrichment (automatic via Serilog)

The host configures Serilog enrichers that add to every log entry:
- `ClinicId` — current tenant (from `ITenantContext`)
- `CorrelationId` — request trace id (from `X-Correlation-Id` header or generated)
- `UserId` — authenticated user's sub claim (GUID, not name)
- `MachineName`, `Environment`, `Application`

Do **not** manually add these — they are already present on every entry.

## Injecting the logger

Use `ILogger<T>` via primary constructor injection. Do not use the static `Log` class.

```csharp
public sealed class RegisterPatientHandler(PatientsDbContext db, ILogger<RegisterPatientHandler> logger)
{
    public async ValueTask<Result<RegisterPatientResponse>> Handle(...)
    {
        // ...
        logger.LogInformation("Patient registered. PatientId={PatientId}", patient.Id);
    }
}
```

## What NOT to log

- Successful reads of PHI records — the **audit trail** (see `auditing.md`) handles "who read what".
  Logging it again creates a PHI-in-logs violation.
- Passwords, tokens, API keys — ever.
- Full request/response bodies for clinical endpoints — they contain PHI.

## Self-check

- [ ] All log calls use message templates (not `$"..."` interpolation).
- [ ] No PHI in any log message at any level.
- [ ] Used the correct log level for the situation.
- [ ] Did not manually add ClinicId/UserId/CorrelationId (enrichers handle it).
