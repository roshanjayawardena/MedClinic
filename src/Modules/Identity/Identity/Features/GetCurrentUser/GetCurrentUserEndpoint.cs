using Identity.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Features.GetCurrentUser;

internal static class GetCurrentUserEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/me", Handle)
            .WithName("GetCurrentUser")
            .WithTags("Identity")
            .WithSummary("Return the authenticated user's profile, roles, and permissions")
            .RequireAuthorization()
            .Produces<GetCurrentUserResponse>()
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> Handle(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCurrentUserQuery(), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Unauthorized();
    }
}
