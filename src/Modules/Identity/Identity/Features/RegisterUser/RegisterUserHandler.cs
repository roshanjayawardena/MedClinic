using Core;
using Identity.Contracts;
using Identity.Domain;
using Mediator;
using Microsoft.AspNetCore.Identity;

namespace Identity.Features.RegisterUser;

public sealed class RegisterUserHandler(
    UserManager<ClinicUser> userManager,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<RegisterUserCommand, Result<RegisterUserResponse>>
{
    public async ValueTask<Result<RegisterUserResponse>> Handle(
        RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        var user = new ClinicUser
        {
            Id = Guid.NewGuid(),
            Email = command.Email,
            UserName = command.Email,
            FirstName = command.FirstName,
            LastName = command.LastName,
            ClinicId = tenantContext.TenantId,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        var createResult = await userManager.CreateAsync(user, command.Password).ConfigureAwait(false);

        if (!createResult.Succeeded)
        {
            var identityError = createResult.Errors.First();
            return Result<RegisterUserResponse>.Fail(
                new Error($"Auth.{identityError.Code}", identityError.Description));
        }

        await userManager.AddToRoleAsync(user, command.Role).ConfigureAwait(false);

        return Result<RegisterUserResponse>.Ok(new RegisterUserResponse(user.Id, user.Email!, command.Role));
    }
}
