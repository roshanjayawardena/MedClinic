using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Patients.Contracts;
using Web;

namespace Patients.Features.RegisterPatient;

internal static class RegisterPatientEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/patients", Handle)
            .WithName("RegisterPatient")
            .WithTags("Patients")
            .WithSummary("Register a new patient at this clinic")
            .AddEndpointFilter<ValidationFilter<RegisterPatientCommand>>()
            .Produces<RegisterPatientResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
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
