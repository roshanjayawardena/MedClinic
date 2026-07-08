using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Patients.Contracts;

namespace Patients.Features.AddAllergy;

internal static class AddAllergyEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/patients/{patientId:guid}/allergies", Handle)
            .WithName("AddAllergy")
            .WithTags("Patients")
            .WithSummary("Record a drug allergy for a patient")
            .Produces<AddAllergyResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        Guid patientId,
        AddAllergyRequest body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new AddAllergyCommand(patientId, body.DrugName, body.Severity, body.Notes);
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/patients/{patientId}/allergies/{result.Value.AllergyId}", result.Value)
            : result.Error!.Code == "Patient.NotFound"
                ? TypedResults.NotFound()
                : TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [result.Error.Code] = [result.Error.Message],
                });
    }
}

internal sealed record AddAllergyRequest(string DrugName, string Severity, string? Notes);
