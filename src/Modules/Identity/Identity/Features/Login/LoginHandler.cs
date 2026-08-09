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
    private static readonly Action<ILogger, string, Exception?> LogFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "LoginFailed"),
            "Login failed: {Reason}");

    private static readonly Action<ILogger, string, Exception?> LogSucceeded =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(2, "LoginSucceeded"),
            "Login succeeded: Role={Role}");

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
            LogFailed(logger, "UserNotFound", null);
            metrics.LoginFailed.Add(1);
            return Result<LoginResponse>.Fail(
                new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        var passwordValid = await userManager
            .CheckPasswordAsync(user, command.Password)
            .ConfigureAwait(false);

        if (!passwordValid)
        {
            LogFailed(logger, "InvalidPassword", null);
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

        LogSucceeded(logger, string.Join(',', roles), null);
        metrics.LoginSuccess.Add(1);

        return Result<LoginResponse>.Ok(
            new LoginResponse(accessToken, refreshToken.Token, "Bearer", expiresIn));
    }
}
