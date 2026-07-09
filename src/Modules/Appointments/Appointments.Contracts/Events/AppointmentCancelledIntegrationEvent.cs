using Mediator;

namespace Appointments.Contracts.Events;

public sealed record AppointmentCancelledIntegrationEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ClinicId,
    DateTimeOffset CancelledAt,
    DateTimeOffset OccurredAt) : INotification;
