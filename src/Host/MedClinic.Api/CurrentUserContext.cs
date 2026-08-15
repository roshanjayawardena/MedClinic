using System.IdentityModel.Tokens.Jwt;
using Core;

namespace MedClinic.Api;

/// <summary>
/// Reads the current user's id from HttpContext claims.
/// Returns Guid.Empty for unauthenticated requests and background jobs —
/// callers (BaseDbContext) handle the empty case gracefully.
/// </summary>
public sealed class CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    : ICurrentUserContext
{
    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;

    public Guid UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User?
                .FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }
}
