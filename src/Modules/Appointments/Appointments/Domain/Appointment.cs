using Core;

namespace Appointments.Domain;

public sealed class Appointment : AuditableEntity
{
    private Appointment() { } // required by EF Core

    public Guid PatientId { get; private set; }
    public DateTimeOffset ScheduledAt { get; private set; }
    public int DurationMinutes { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public AppointmentStatus Status { get; private set; }
    public DateTimeOffset? CheckedInAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public static Appointment Book(
        Guid patientId,
        DateTimeOffset scheduledAt,
        int durationMinutes,
        string reason) =>
        new()
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            ScheduledAt = scheduledAt,
            DurationMinutes = durationMinutes,
            Reason = reason,
            Status = AppointmentStatus.Scheduled,
        };

    public Result CheckIn(DateTimeOffset now)
    {
        if (Status != AppointmentStatus.Scheduled)
            return Result.Fail(new Error("Appointment.InvalidStatus",
                $"Cannot check in from status '{Status}'. Expected 'Scheduled'."));

        Status = AppointmentStatus.CheckedIn;
        CheckedInAt = now;
        return Result.Ok();
    }

    public Result Complete(DateTimeOffset now)
    {
        if (Status != AppointmentStatus.CheckedIn)
            return Result.Fail(new Error("Appointment.InvalidStatus",
                $"Cannot complete from status '{Status}'. Expected 'CheckedIn'."));

        Status = AppointmentStatus.Completed;
        CompletedAt = now;
        return Result.Ok();
    }

    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (Status is AppointmentStatus.Completed or AppointmentStatus.Cancelled)
            return Result.Fail(new Error("Appointment.InvalidStatus",
                $"Cannot cancel an appointment with status '{Status}'."));

        Status = AppointmentStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = now;
        return Result.Ok();
    }
}
