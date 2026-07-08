using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Identity.Services;

public interface ICurrentUser
{
    Guid UserId { get; }
    Guid ClinicId { get; }
    string Email { get; }
    string FirstName { get; }
    string LastName { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool IsAuthenticated { get; }
    bool HasPermission(string permission);
}

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId =>
        Guid.TryParse(Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
            ? id : Guid.Empty;

    public Guid ClinicId =>
        Guid.TryParse(Principal?.FindFirst("clinic_id")?.Value, out var id)
            ? id : Guid.Empty;

    public string Email => Principal?.FindFirst(JwtRegisteredClaimNames.Email)?.Value ?? string.Empty;

    public string FirstName =>
        Principal?.FindFirst(JwtRegisteredClaimNames.GivenName)?.Value ?? string.Empty;

    public string LastName =>
        Principal?.FindFirst(JwtRegisteredClaimNames.FamilyName)?.Value ?? string.Empty;

    public IReadOnlyList<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    public IReadOnlyList<string> Permissions =>
        Principal?.FindAll("permissions").Select(c => c.Value).ToList() ?? [];

    public bool HasPermission(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}
