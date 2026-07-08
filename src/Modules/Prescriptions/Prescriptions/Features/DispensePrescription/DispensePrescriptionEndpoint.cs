using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Prescriptions.Contracts;

namespace Prescriptions.Features.DispensePrescription;

internal static class DispensePrescriptionEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/prescriptions/{id:guid}/dispense", Handle)
            .WithName("DispensePrescription")
            .WithTags("Prescriptions")
            .WithSummary("Dispense an active prescription (pharmacist action)")
            .Produces<DispensePrescriptionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DispensePrescriptionCommand(id), cancellationToken);

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
