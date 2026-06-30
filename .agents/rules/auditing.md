# Auditing — clinical-record access trail

Read this before working on Encounters, Prescriptions, or any read/write of patient clinical data.
In health software an audit trail is often a legal requirement, not a nice-to-have.

## What must be audited

Every **read or write** of an `Encounter` or `Prescription` emits an audit event. Writes to patient
demographics and consent are audited too. Routine list/search of low-sensitivity data need not be, but
opening a specific clinical record does.

## The audit event

Capture, at minimum:

| Field | Source |
|---|---|
| `ActorId` | the authenticated staff member |
| `Action` | `Read` / `Created` / `Updated` / `Deleted` |
| `EntityType` + `EntityId` | e.g. `Encounter`, the record's Id |
| `ClinicId` | the current tenant |
| `OccurredAt` | `TimeProvider.GetUtcNow()` — never `DateTime.Now` |
| `CorrelationId` | the request's correlation id |

**No PHI in the audit event itself** — store the entity *Id*, never the patient's name, diagnosis, or
medication. The audit row records *that* a record was accessed, not its clinical contents.

## How it's emitted

- Prefer the **Outbox**: enqueue the audit event in the same transaction as the operation, so a write and
  its audit entry commit together (no audited-but-not-saved, no saved-but-not-audited).
- For pure reads, record the audit entry on the read path (an action filter or the handler), still via
  the outbox/queue so the read isn't blocked on the audit sink.
- The `Auditing` module owns the audit store and consumes these events. Other modules emit; they don't
  write the audit table directly.

```csharp
// inside a handler, in the same unit of work as the change
auditEvents.Enqueue(new ClinicalRecordAudited(
    actor.Id, AuditAction.Updated, nameof(Encounter), encounter.Id, tenant.ClinicId,
    timeProvider.GetUtcNow(), correlation.Id));
```

## Immutability & retention

- Audit rows are append-only — never updated or hard-deleted.
- Even a soft-delete of a clinical record is itself an audited `Deleted` action.

## Self-check (the phi-review workflow enforces this)

- [ ] Every Encounter/Prescription read and write emits an audit event.
- [ ] The audit event carries Ids and metadata only — no PHI.
- [ ] Writes audit in the same transaction (outbox), not best-effort after the fact.
- [ ] Audit rows are append-only.
