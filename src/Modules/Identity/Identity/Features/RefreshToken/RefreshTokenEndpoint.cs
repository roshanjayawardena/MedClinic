using Identity.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Identity.Features.RefreshToken;

public static class RefreshTokenEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/refresh", async (
            RefreshTokenCommand command,
            IMediator mediator,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct).ConfigureAwait(false);
            return result.IsSuccess
                ? TypedResults.Ok(result.Value)
                : result.Error.ToProblemResult(ctx);
        })
        .AllowAnonymous()
        .WithName("RefreshToken")
        .WithTags("Auth")
        .WithSummary("Exchange an expired access token + refresh token for a new pair");
}
