using Core;
using Mediator;

namespace Identity.Contracts;

public sealed record ImpersonateUserCommand(Guid TargetUserId)
    : IRequest<Result<ImpersonateUserResponse>>;

public sealed record ImpersonateUserResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    Guid ImpersonatedUserId);
