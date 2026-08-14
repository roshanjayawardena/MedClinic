using Encounters.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Encounters.Features.ListEncountersByPatient;

internal static class ListEncountersByPatientEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/patients/{patientId:guid}/encounters", Handle)
            .WithName("ListEncountersByPatient")
            .WithTags("Encounters")
            .WithSummary("List all encounters for a patient")
            .Produces<IReadOnlyList<EncounterSummaryDto>>()
            .RequireAuthorization();
    }

    private static async Task<IResult> Handle(
        Guid patientId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListEncountersByPatientQuery(patientId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.NotFound();
    }
}
