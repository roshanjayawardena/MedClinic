using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Core;
using Identity.Contracts;
using Identity.Domain;
using Identity.Services;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Features.ImpersonateUser;

/// <summary>
/// Issues a short-lived (15-min) access token that acts as the target user.
/// The token carries an "acting_as" claim so audit logs can distinguish impersonated actions.
/// Restricted to Admin role only.
/// </summary>
public sealed class ImpersonateHandler(
    UserManager<ClinicUser> userManager,
    ICurrentUser currentUser,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ImpersonateHandler> logger)
    : IRequestHandler<ImpersonateUserCommand, Result<ImpersonateUserResponse>>
{
    public async ValueTask<Result<ImpersonateUserResponse>> Handle(
        ImpersonateUserCommand command,
        CancellationToken cancellationToken)
    {
        var target = await userManager.FindByIdAsync(command.TargetUserId.ToString())
            .ConfigureAwait(false);

        if (target is null || target.ClinicId != currentUser.ClinicId)
            return Result<ImpersonateUserResponse>.Fail(
                new Error("Users.NotFound", "Target user not found in this clinic."));

        if (!target.IsActive)
            return Result<ImpersonateUserResponse>.Fail(
                new Error("Users.Inactive", "Cannot impersonate an inactive user."));

        var targetRoles = await userManager.GetRolesAsync(target).ConfigureAwait(false);
        var permissions = targetRoles
            .SelectMany(r => RolePermissions.ByRole.TryGetValue(r, out var p) ? p : [])
            .Distinct()
            .ToArray();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,        target.Id.ToString()),
            new(JwtRegisteredClaimNames.GivenName,  target.FirstName),
            new(JwtRegisteredClaimNames.FamilyName, target.LastName),
            new("clinic_id",  target.ClinicId.ToString()),
            new("acting_as",  target.Id.ToString()),
            new("impersonated_by", currentUser.UserId.ToString()),
        };

        claims.AddRange(targetRoles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim("permissions", p)));

        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Secret"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now         = timeProvider.GetUtcNow();

        var token = new JwtSecurityToken(
            issuer:             configuration["Jwt:Issuer"],
            audience:           configuration["Jwt:Audience"],
            claims:             claims,
            notBefore:          now.UtcDateTime,
            expires:            now.AddMinutes(15).UtcDateTime,  // short-lived
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        logger.LogWarning(
            "Impersonation: Admin={AdminId} impersonating User={TargetId}",
            currentUser.UserId, target.Id);

        return Result<ImpersonateUserResponse>.Ok(new ImpersonateUserResponse(
            tokenString, "Bearer", 900, target.Id));
    }
}
