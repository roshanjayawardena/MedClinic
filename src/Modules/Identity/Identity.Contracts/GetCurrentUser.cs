using Core;
using Mediator;

namespace Identity.Contracts;

public sealed record GetCurrentUserQuery : IRequest<Result<GetCurrentUserResponse>>;

public sealed record GetCurrentUserResponse(
    Guid UserId,
    Guid ClinicId,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
