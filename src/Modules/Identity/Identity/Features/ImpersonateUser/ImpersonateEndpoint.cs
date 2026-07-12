using Identity.Contracts;
using Identity.Domain;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Identity.Features.ImpersonateUser;

public static class ImpersonateEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/impersonate", async (
            ImpersonateUserCommand command,
            IMediator mediator,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct).ConfigureAwait(false);
            return result.IsSuccess
                ? TypedResults.Ok(result.Value)
                : result.Error.ToProblemResult(ctx);
        })
        .RequireAuthorization(Permissions.UsersManage)
        .WithName("ImpersonateUser")
        .WithTags("Auth")
        .WithSummary("Admin: issue a short-lived token acting as another user (15 min)");
}
