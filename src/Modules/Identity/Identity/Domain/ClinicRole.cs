using Microsoft.AspNetCore.Identity;

namespace Identity.Domain;

public sealed class ClinicRole : IdentityRole<Guid>
{
    public ClinicRole() { }
    public ClinicRole(string roleName) : base(roleName) { }
}
