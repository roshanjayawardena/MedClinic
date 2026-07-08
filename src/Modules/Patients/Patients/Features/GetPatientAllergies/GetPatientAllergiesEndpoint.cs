using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Patients.Contracts;

namespace Patients.Features.GetPatientAllergies;

internal static class GetPatientAllergiesEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/patients/{patientId:guid}/allergies", Handle)
            .WithName("GetPatientAllergies")
            .WithTags("Patients")
            .WithSummary("Get all recorded drug allergies for a patient")
            .Produces<GetPatientAllergiesResponse>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Guid patientId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPatientAllergiesQuery(patientId), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}
