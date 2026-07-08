using Microsoft.AspNetCore.Identity;

namespace Identity.Domain;

public sealed class ClinicUser : IdentityUser<Guid>
{
    public Guid ClinicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
