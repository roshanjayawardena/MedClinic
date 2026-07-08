using Core;
using Mediator;

namespace Identity.Contracts;

public sealed record RegisterUserCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string Role) : IRequest<Result<RegisterUserResponse>>;

public sealed record RegisterUserResponse(Guid UserId, string Email, string Role);
