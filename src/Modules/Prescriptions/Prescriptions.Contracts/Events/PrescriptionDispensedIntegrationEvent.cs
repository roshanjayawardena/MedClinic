using Mediator;

namespace Prescriptions.Contracts.Events;

/// <summary>
/// Published when a pharmacist dispenses a prescription.
/// Billing subscribes to create a consultation invoice.
/// DrugName is intentionally excluded — it is PHI and must not transit integration events.
/// </summary>
public sealed record PrescriptionDispensedIntegrationEvent(
    Guid PrescriptionId,
    Guid EncounterId,
    Guid PatientId,
    Guid ClinicId,
    DateTimeOffset DispensedAt,
    DateTimeOffset OccurredAt) : INotification;
