using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Prescriptions.Contracts;
using Web;

namespace Prescriptions.Features.WritePrescription;

internal static class WritePrescriptionEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/prescriptions", Handle)
            .WithName("WritePrescription")
            .WithTags("Prescriptions")
            .WithSummary("Write a new prescription against a closed encounter")
            .AddEndpointFilter<ValidationFilter<WritePrescriptionCommand>>()
            .Produces<WritePrescriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        WritePrescriptionCommand command,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/prescriptions/{result.Value.PrescriptionId}", result.Value)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.Error!.Code] = [result.Error.Message],
            });
    }
}
