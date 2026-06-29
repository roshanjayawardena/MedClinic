using Core;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Patients.Contracts;
using Web;

namespace Patients;

public static class PatientsEndpoints
{
    public static IEndpointRouteBuilder MapPatientsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/patients", RegisterPatient)
            .WithName("RegisterPatient")
            .WithTags("Patients")
            .WithSummary("Register a new patient")
            .AddEndpointFilter<ValidationFilter<RegisterPatientCommand>>()
            .Produces<RegisterPatientResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> RegisterPatient(
        RegisterPatientCommand command,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/patients/{result.Value.PatientId}", result.Value)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.Error!.Code] = [result.Error.Message],
            });
    }
}
