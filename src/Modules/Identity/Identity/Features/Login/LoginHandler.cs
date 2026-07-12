using Core;
using Identity.Contracts;
using Identity.Domain;
using Identity.Persistence;
using Identity.Services;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Identity.Features.Login;

public sealed class LoginHandler(
    UserManager<ClinicUser> userManager,
    IJwtService jwtService,
    IdentityModuleDbContext db,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ClinicMetrics metrics,
    ILogger<LoginHandler> logger)
    : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    public async ValueTask<Result<LoginResponse>> Handle(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        // FindByEmailAsync applies the ClinicId query filter — a user in ClinicA
        // cannot be found when the X-Tenant-Id header identifies ClinicB.
        var user = await userManager.FindByEmailAsync(command.Email).ConfigureAwait(false);

        if (user is null || !user.IsActive)
        {
            // Never log command.Email — it is PHI. Log only the failure category.
            logger.LogWarning("Login failed: {Reason}", "UserNotFound");
            metrics.LoginFailed.Add(1);
            return Result<LoginResponse>.Fail(
                new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        var passwordValid = await userManager
            .CheckPasswordAsync(user, command.Password)
            .ConfigureAwait(false);

        if (!passwordValid)
        {
            logger.LogWarning("Login failed: {Reason}", "InvalidPassword");
            metrics.LoginFailed.Add(1);
            return Result<LoginResponse>.Fail(
                new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        var roles        = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        var accessToken  = jwtService.GenerateToken(user, roles);
        var expiresIn    = configuration.GetValue<int>("Jwt:ExpiryMinutes", 60) * 60;

        var refreshLifetime = TimeSpan.FromDays(configuration.GetValue<int>("Jwt:RefreshTokenDays", 30));
        var refreshToken    = Identity.Domain.RefreshToken.Create(user.Id, user.ClinicId, refreshLifetime, timeProvider);
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Login succeeded: Role={Role}", string.Join(',', roles));
        metrics.LoginSuccess.Add(1);

        return Result<LoginResponse>.Ok(
            new LoginResponse(accessToken, refreshToken.Token, "Bearer", expiresIn));
    }
}
