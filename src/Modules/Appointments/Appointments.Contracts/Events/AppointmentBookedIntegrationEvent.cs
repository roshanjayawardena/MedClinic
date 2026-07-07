using Mediator;

namespace Appointments.Contracts.Events;

/// <summary>
/// Published after an appointment is successfully booked.
/// Subscribers: Notifications (schedule reminder).
/// Payload carries Ids only — no PHI.
/// </summary>
public sealed record AppointmentBookedIntegrationEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ClinicId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset OccurredAt) : INotification;
