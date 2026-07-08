using Identity.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Identity.Features.Login;

internal static class LoginEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", Handle)
            .WithName("Login")
            .WithTags("Identity")
            .WithSummary("Authenticate and receive a JWT bearer token")
            .AddEndpointFilter<ValidationFilter<LoginCommand>>()
            .RequireRateLimiting("auth")
            .AllowAnonymous()
            .Produces<LoginResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        LoginCommand command,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Unauthorized();
    }
}
