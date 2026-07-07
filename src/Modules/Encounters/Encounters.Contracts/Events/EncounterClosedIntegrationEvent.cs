using Mediator;

namespace Encounters.Contracts.Events;

/// <summary>
/// Published when a clinical encounter is closed.
/// Subscribers: Prescriptions (an active encounter is required before a script can be written).
/// Payload carries Ids only — no PHI.
/// </summary>
public sealed record EncounterClosedIntegrationEvent(
    Guid EncounterId,
    Guid PatientId,
    Guid AppointmentId,
    Guid ClinicId,
    DateTimeOffset OccurredAt) : INotification;
