using Core;
using Mediator;

namespace Identity.Contracts;

public sealed record RefreshTokenCommand(string AccessToken, string RefreshToken)
    : IRequest<Result<RefreshTokenResponse>>;

public sealed record RefreshTokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn);
