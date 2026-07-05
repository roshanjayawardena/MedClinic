# Module: Appointments

Owns the scheduling lifecycle — booking, check-in, check-out, and cancellation. It does not own
clinical data (that is Encounters) or patient demographics (that is Patients).

Read `architecture.md`, `database.md`, `eventing.md`, and `phi-and-tenancy.md` before working here.

---

## Responsibility

| In scope | Out of scope |
|---|---|
| Slot availability and booking | Clinical notes, diagnoses |
| Check-in / check-out lifecycle | Patient demographics |
| Appointment-level reminders (integration event) | SMS/email delivery (Notifications module) |
| Cancelled/no-show recording | Billing (triggered by `AppointmentCompletedIntegrationEvent`) |

---

## Appointment aggregate — status machine

```
Scheduled → Confirmed → CheckedIn → Completed
         ↘ Cancelled   ↗            ↘ NoShow
```

- State transitions are methods on `Appointment` (`Confirm()`, `CheckIn()`, `Complete()`, `Cancel(reason)`).
- Illegal transitions return `Result.Fail(...)` — the aggregate never throws for business rule violations.
- `Cancel(reason)` is the **only** way to cancel; reason is required and stored.
- **No direct property assignment** for status — always go through the transition method.

```csharp
public sealed class Appointment : AuditableEntity
{
    public Guid PatientId { get; private set; }    // FK to Patients (cross-module, Id only)
    public DateTimeOffset ScheduledAt { get; private set; }
    public int DurationMinutes { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public string? CancellationReason { get; private set; }

    public Result CheckIn()
    {
        if (Status != AppointmentStatus.Confirmed)
            return Result.Fail("Only confirmed appointments can be checked in.");
        Status = AppointmentStatus.CheckedIn;
        return Result.Ok();
    }
}
```

---

## Layout

```
src/Modules/Appointments/
├── Appointments/
│   ├── AppointmentsModule.cs
│   ├── Domain/
│   │   ├── Appointment.cs
│   │   └── AppointmentStatus.cs
│   ├── Persistence/
│   │   ├── AppointmentsDbContext.cs
│   │   └── Configurations/AppointmentConfiguration.cs
│   └── Features/
│       ├── BookAppointment/
│       ├── ConfirmAppointment/
│       ├── CheckInAppointment/
│       ├── CompleteAppointment/
│       ├── CancelAppointment/
│       ├── GetAppointments/       (paginated list — doctor's daily schedule)
│       └── GetAppointment/        (single, for check-in view)
└── Appointments.Contracts/
    ├── BookAppointment.cs
    ├── CancelAppointment.cs
    ├── Events/
    │   ├── AppointmentBookedIntegrationEvent.cs
    │   ├── AppointmentCancelledIntegrationEvent.cs
    │   └── AppointmentCompletedIntegrationEvent.cs
    └── AppointmentExistsQuery.cs  (cross-module: Encounters validates this)
```

---

## Cross-module dependencies

- **Reads from Patients** via `PatientExistsQuery` (Contracts) before booking — never touches Patients runtime.
- **Publishes** integration events (see `eventing.md`) on Booked / Cancelled / Completed transitions.
- **Does not** read Encounters or Prescriptions.

---

## Module-specific gotchas

- **Double-booking guard:** a patient cannot have two `Scheduled`/`Confirmed` appointments that overlap.
  Check in the `BookAppointment` handler before persisting.
- **Reminders are NOT sent here.** The handler publishes `AppointmentBookedIntegrationEvent`; the
  Notifications module subscribes and sends the reminder.
- **PHI in appointment data:** `PatientId` is a GUID — safe to log. The patient's name is NOT stored
  here; callers join through the Patients module if display name is needed.
- **Tenant scoping:** `BaseDbContext` handles the global query filter. Never disable it.
