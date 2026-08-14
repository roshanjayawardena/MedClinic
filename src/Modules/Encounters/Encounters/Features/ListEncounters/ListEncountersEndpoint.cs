using Encounters.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Encounters.Features.ListEncounters;

internal static class ListEncountersEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/encounters", Handle)
            .WithName("ListEncounters")
            .WithTags("Encounters")
            .WithSummary("List all encounters (paged)")
            .Produces<ListEncountersResponse>()
            .RequireAuthorization();
    }

    private static async Task<IResult> Handle(
        int page,
        int pageSize,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListEncountersQuery(page, pageSize), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem("Failed to list encounters");
    }
}
