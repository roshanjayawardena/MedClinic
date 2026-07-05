# Eventing — domain events & cross-module integration events

Read this before publishing an event or reacting to one across module boundaries.

## Two kinds of events

| Kind | Scope | Transport | When to use |
|---|---|---|---|
| **Domain event** | Within one module | In-process (dispatch after SaveChanges) | State transition the aggregate needs to announce locally |
| **Integration event** | Cross-module | Outbox → message bus | One module needs to react to something that happened in another |

Never route a domain event across module boundaries. If Appointments needs to react to `PatientRegistered`,
that is an integration event — the Patients module publishes it to the Outbox; Appointments subscribes.

---

## Domain events

- Declared in the module's `Domain/` folder, **not** in Contracts.
- Raised by the aggregate (inside an intent method), not by the handler.
- Dispatched by the infrastructure after `SaveChanges` succeeds.
- No async side effects in domain event handlers — keep them fast and in-memory.

```csharp
// Domain/PatientRegisteredEvent.cs  (Patients module, internal)
public sealed record PatientRegisteredEvent(Guid PatientId, Guid ClinicId, DateTimeOffset OccurredAt);
```

```csharp
// Domain/Patient.cs — aggregate raises the event
public Result Register(...)
{
    // ... validation ...
    RaiseDomainEvent(new PatientRegisteredEvent(Id, TenantId, _clock.GetUtcNow()));
    return Result.Ok();
}
```

---

## Integration events

- Contract lives in the **publishing** module's `.Contracts` project — that is the only shared surface.
- The **handler** publishes via the Outbox inside the same transaction as the business change.
  A publish that isn't in the same transaction can silently drop the event on a crash.
- Subscribers live in their own module; they handle idempotently (check if already processed).

```
// Patients.Contracts/Events/PatientRegisteredIntegrationEvent.cs
public sealed record PatientRegisteredIntegrationEvent(
    Guid PatientId,
    Guid ClinicId,
    DateTimeOffset OccurredAt);
```

```csharp
// Patients handler — publish via Outbox in same unit of work
outbox.Publish(new PatientRegisteredIntegrationEvent(patient.Id, tenant.ClinicId, now));
await db.SaveChangesAsync(ct).ConfigureAwait(false);
```

```csharp
// Appointments module subscriber — idempotent
public sealed class OnPatientRegistered(AppointmentsDbContext db)
{
    public async Task Handle(PatientRegisteredIntegrationEvent e, CancellationToken ct)
    {
        if (await db.PatientProfiles.AnyAsync(p => p.PatientId == e.PatientId, ct))
            return; // already handled — idempotent guard
        // ... create local read model ...
    }
}
```

---

## Outbox guarantee

- **Transactional**: event row is written in the same `SaveChanges` as the business operation.
  No separate saves — they commit together or roll back together.
- **At-least-once**: the relay dispatches and may re-deliver; all subscribers must be idempotent.
- **Tenant context preserved**: the outbox row carries `ClinicId`; the relay restores the tenant before
  invoking the subscriber.

---

## MedClinic integration event catalogue

| Publisher | Event | Subscribers |
|---|---|---|
| Patients | `PatientRegisteredIntegrationEvent` | Appointments (validate booking target), Notifications (welcome) |
| Appointments | `AppointmentBookedIntegrationEvent` | Notifications (send reminder) |
| Appointments | `AppointmentCancelledIntegrationEvent` | Notifications (cancel reminder), Billing (void draft invoice) |
| Appointments | `AppointmentCompletedIntegrationEvent` | Billing (create invoice) |
| Encounters | `EncounterClosedIntegrationEvent` | Prescriptions (unlock for dispensing) |

---

## Self-check

- [ ] Domain events are module-private; integration events live in `.Contracts`.
- [ ] Integration event published in the **same** `SaveChanges` call as the business change.
- [ ] Subscriber guards against duplicate delivery (idempotency key or existence check).
- [ ] Tenant context is preserved across the Outbox relay.
- [ ] No PHI in event payloads — carry Ids only (see `phi-and-tenancy.md`).
