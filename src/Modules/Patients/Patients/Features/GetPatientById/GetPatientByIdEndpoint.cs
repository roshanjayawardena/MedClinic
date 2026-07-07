using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Patients.Contracts;

namespace Patients.Features.GetPatientById;

internal static class GetPatientByIdEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/patients/{id:guid}", Handle)
            .WithName("GetPatientById")
            .WithTags("Patients")
            .WithSummary("Get a patient record by ID")
            .Produces<GetPatientByIdResponse>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPatientByIdQuery(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}
