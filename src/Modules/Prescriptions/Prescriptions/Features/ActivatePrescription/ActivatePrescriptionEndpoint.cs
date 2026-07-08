using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Prescriptions.Contracts;

namespace Prescriptions.Features.ActivatePrescription;

internal static class ActivatePrescriptionEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/prescriptions/{id:guid}/activate", Handle)
            .WithName("ActivatePrescription")
            .WithTags("Prescriptions")
            .WithSummary("Activate a Draft prescription so the pharmacist can dispense it")
            .Produces<ActivatePrescriptionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ActivatePrescriptionCommand(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error!.Code == "Prescription.NotFound"
                ? TypedResults.NotFound()
                : TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [result.Error.Code] = [result.Error.Message],
                });
    }
}
