using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Prescriptions.Contracts;

namespace Prescriptions.Features.GetPrescriptionById;

internal static class GetPrescriptionByIdEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/prescriptions/{id:guid}", Handle)
            .WithName("GetPrescriptionById")
            .WithTags("Prescriptions")
            .WithSummary("Get a prescription by ID (access is audit-logged)")
            .Produces<GetPrescriptionByIdResponse>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPrescriptionByIdQuery(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}
