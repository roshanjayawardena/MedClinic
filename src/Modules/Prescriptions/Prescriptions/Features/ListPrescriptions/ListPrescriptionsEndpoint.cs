using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Prescriptions.Contracts;

namespace Prescriptions.Features.ListPrescriptions;

internal static class ListPrescriptionsEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/prescriptions", Handle)
            .WithName("ListPrescriptions")
            .WithTags("Prescriptions")
            .WithSummary("List all prescriptions (paged)")
            .Produces<ListPrescriptionsResponse>()
            .RequireAuthorization();
    }

    private static async Task<IResult> Handle(
        int page,
        int pageSize,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPrescriptionsQuery(page, pageSize), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem("Failed to list prescriptions");
    }
}
