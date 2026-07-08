using Core;

namespace Patients.Domain;

public sealed class Allergy : AuditableEntity
{
    private Allergy() { }

    public Guid PatientId { get; private set; }
    public string DrugName { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public string? Notes { get; private set; }

    public static Allergy Record(Guid patientId, string drugName, string severity, string? notes) =>
        new()
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            DrugName = drugName,
            Severity = severity,
            Notes = notes,
        };
}
