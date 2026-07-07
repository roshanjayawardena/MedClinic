using Encounters.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Encounters.Features.CloseEncounter;

internal static class CloseEncounterEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/encounters/{id:guid}/close", Handle)
            .WithName("CloseEncounter")
            .WithTags("Encounters")
            .WithSummary("Close a clinical encounter — enables prescription writing")
            .Produces<CloseEncounterResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        Guid id,
        CloseEncounterRequest body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CloseEncounterCommand(id, body.ClinicalNotes), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error!.Code == "Encounter.NotFound"
                ? TypedResults.NotFound()
                : TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [result.Error.Code] = [result.Error.Message],
                });
    }
}

internal sealed record CloseEncounterRequest(string? ClinicalNotes = null);
