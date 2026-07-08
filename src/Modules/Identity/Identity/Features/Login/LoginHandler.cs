using Core;
using Identity.Contracts;
using Identity.Domain;
using Identity.Services;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace Identity.Features.Login;

public sealed class LoginHandler(
    UserManager<ClinicUser> userManager,
    IJwtService jwtService,
    IConfiguration configuration)
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
            return Result<LoginResponse>.Fail(
                new Error("Auth.InvalidCredentials", "Invalid email or password."));

        var passwordValid = await userManager
            .CheckPasswordAsync(user, command.Password)
            .ConfigureAwait(false);

        if (!passwordValid)
            return Result<LoginResponse>.Fail(
                new Error("Auth.InvalidCredentials", "Invalid email or password."));

        var roles = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        var token = jwtService.GenerateToken(user, roles);
        var expiresIn = configuration.GetValue<int>("Jwt:ExpiryMinutes", 60) * 60;

        return Result<LoginResponse>.Ok(new LoginResponse(token, "Bearer", expiresIn));
    }
}
