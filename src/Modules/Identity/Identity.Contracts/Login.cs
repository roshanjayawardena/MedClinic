using Core;
using Mediator;

namespace Identity.Contracts;

public sealed record LoginCommand(string Email, string Password) : IRequest<Result<LoginResponse>>;

public sealed record LoginResponse(string AccessToken, string TokenType, int ExpiresIn);
