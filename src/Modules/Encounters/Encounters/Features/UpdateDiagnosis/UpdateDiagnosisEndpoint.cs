using Encounters.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Encounters.Features.UpdateDiagnosis;

internal static class UpdateDiagnosisEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/encounters/{id:guid}/diagnoses/{icd10Code}", Handle)
            .WithName("UpdateDiagnosis")
            .WithTags("Encounters")
            .WithSummary("Update description/type of a diagnosis on an open encounter")
            .Produces<UpdateDiagnosisResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        Guid id,
        string icd10Code,
        UpdateDiagnosisRequest body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new UpdateDiagnosisCommand(id, icd10Code, body.Description, body.DiagnosisType);
        var result = await mediator.Send(command, cancellationToken);

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

internal sealed record UpdateDiagnosisRequest(string Description, string DiagnosisType);
