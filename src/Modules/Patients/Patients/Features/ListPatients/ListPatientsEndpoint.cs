using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Patients.Contracts;

namespace Patients.Features.ListPatients;

internal static class ListPatientsEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/patients", Handle)
            .WithName("ListPatients")
            .WithTags("Patients")
            .WithSummary("List patients with pagination")
            .Produces<ListPatientsResponse>();
    }

    private static async Task<IResult> Handle(
        int page,
        int pageSize,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListPatientsQuery(page, pageSize), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem("Failed to list patients.", statusCode: 500);
    }
}
