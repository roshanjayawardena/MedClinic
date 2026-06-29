namespace Patients;

public sealed class Patient
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public DateOnly DateOfBirth { get; private set; }
    public string ContactPhone { get; private set; } = string.Empty;
    public bool ConsentToDataProcessing { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private Patient()
    {
    }

    public static Patient Register(
        Guid tenantId,
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        string contactPhone,
        bool consentToDataProcessing,
        DateTimeOffset nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth,
            ContactPhone = contactPhone,
            ConsentToDataProcessing = consentToDataProcessing,
            CreatedAtUtc = nowUtc,
        };
}
