using Encounters.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Encounters.Features.AddDiagnosis;

internal static class AddDiagnosisEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/encounters/{id:guid}/diagnoses", Handle)
            .WithName("AddDiagnosis")
            .WithTags("Encounters")
            .WithSummary("Add an ICD-10 diagnosis to an open encounter")
            .AddEndpointFilter<ValidationFilter<AddDiagnosisCommand>>()
            .Produces<AddDiagnosisResponse>()
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        Guid id,
        AddDiagnosisRequest body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new AddDiagnosisCommand(id, body.Icd10Code, body.Description, body.DiagnosisType);
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

internal sealed record AddDiagnosisRequest(string Icd10Code, string Description, string DiagnosisType);
