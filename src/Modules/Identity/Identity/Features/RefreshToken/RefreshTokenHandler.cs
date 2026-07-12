using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Core;
using Identity.Contracts;
using Identity.Domain;
using Identity.Persistence;
using Identity.Services;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Features.RefreshToken;

public sealed class RefreshTokenHandler(
    IdentityModuleDbContext db,
    UserManager<ClinicUser> userManager,
    IJwtService jwtService,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<RefreshTokenHandler> logger)
    : IRequestHandler<RefreshTokenCommand, Result<RefreshTokenResponse>>
{
    public async ValueTask<Result<RefreshTokenResponse>> Handle(
        RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        var principal = GetPrincipalFromExpiredToken(command.AccessToken, configuration);
        if (principal is null)
            return Result<RefreshTokenResponse>.Fail(
                new Error("Auth.InvalidToken", "Invalid access token."));

        var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == command.RefreshToken && r.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (stored is null || !stored.IsActive(timeProvider))
        {
            logger.LogWarning("Refresh failed: {Reason}", stored is null ? "NotFound" : "TokenInactive");
            return Result<RefreshTokenResponse>.Fail(
                new Error("Auth.InvalidToken", "Refresh token is invalid or expired."));
        }

        var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null || !user.IsActive)
            return Result<RefreshTokenResponse>.Fail(
                new Error("Auth.Unauthorized", "User account is inactive."));

        var roles      = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        var newAccess  = jwtService.GenerateToken(user, roles);
        var lifetime   = TimeSpan.FromDays(configuration.GetValue<int>("Jwt:RefreshTokenDays", 30));
        var newRefresh = Domain.RefreshToken.Create(user.Id, user.ClinicId, lifetime, timeProvider);

        stored.Revoke(replacedBy: newRefresh.Token);
        db.RefreshTokens.Add(newRefresh);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var expiresIn = configuration.GetValue<int>("Jwt:ExpiryMinutes", 60) * 60;
        return Result<RefreshTokenResponse>.Ok(new RefreshTokenResponse(
            newAccess, newRefresh.Token, "Bearer", expiresIn));
    }

    private static ClaimsPrincipal? GetPrincipalFromExpiredToken(string token, IConfiguration cfg)
    {
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = cfg["Jwt:Issuer"],
            ValidateAudience         = true,
            ValidAudience            = cfg["Jwt:Audience"],
            ValidateLifetime         = false,   // allow expired access tokens during refresh
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(cfg["Jwt:Secret"]!)),
        };

        try
        {
            return new JwtSecurityTokenHandler()
                .ValidateToken(token, validationParameters, out _);
        }
        catch
        {
            return null;
        }
    }
}
