using Encounters.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Encounters.Features.RemoveDiagnosis;

internal static class RemoveDiagnosisEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapDelete("/encounters/{id:guid}/diagnoses/{icd10Code}", Handle)
            .WithName("RemoveDiagnosis")
            .WithTags("Encounters")
            .WithSummary("Remove a diagnosis from an open encounter")
            .Produces<RemoveDiagnosisResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        Guid id,
        string icd10Code,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RemoveDiagnosisCommand(id, icd10Code), cancellationToken);

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
