using Core;
using Identity.Contracts;
using Identity.Services;
using Mediator;

namespace Identity.Features.GetCurrentUser;

public sealed class GetCurrentUserHandler(ICurrentUser currentUser)
    : IRequestHandler<GetCurrentUserQuery, Result<GetCurrentUserResponse>>
{
    public ValueTask<Result<GetCurrentUserResponse>> Handle(
        GetCurrentUserQuery query,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return ValueTask.FromResult(
                Result<GetCurrentUserResponse>.Fail(
                    new Error("Auth.Unauthenticated", "No authenticated user found.")));

        var response = new GetCurrentUserResponse(
            currentUser.UserId,
            currentUser.ClinicId,
            currentUser.Email,
            currentUser.FirstName,
            currentUser.LastName,
            currentUser.Roles,
            currentUser.Permissions);

        return ValueTask.FromResult(Result<GetCurrentUserResponse>.Ok(response));
    }
}
