using Core;

namespace Patients.Domain;

public sealed class Patient : AuditableEntity
{
    private Patient() { } // required by EF Core

    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public DateOnly DateOfBirth { get; private set; }
    public string ContactPhone { get; private set; } = string.Empty;
    public bool ConsentToDataProcessing { get; private set; }
    public bool ConsentToCommunications { get; private set; }

    /// <summary>
    /// Creates a new Patient. Id is set here; TenantId and CreatedAt are
    /// stamped automatically by BaseDbContext.SaveChangesAsync — not set here.
    /// Consent is enforced by RegisterPatientValidator before this method runs.
    /// </summary>
    public static Patient Register(
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        string contactPhone,
        bool consentToDataProcessing,
        bool consentToCommunications) =>
        new()
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth,
            ContactPhone = contactPhone,
            ConsentToDataProcessing = consentToDataProcessing,
            ConsentToCommunications = consentToCommunications,
        };
}
