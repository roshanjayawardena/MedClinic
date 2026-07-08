namespace Prescriptions.Domain;

/// <summary>
/// Local read model populated by OnEncounterClosedHandler.
/// Allows WritePrescription to verify a closed encounter exists
/// without querying across module boundaries at runtime.
/// </summary>
public sealed class ClosedEncounterRecord
{
    public Guid EncounterId { get; init; }
    public Guid PatientId { get; init; }
    public Guid TenantId { get; init; }
    public DateTimeOffset ClosedAt { get; init; }
}
