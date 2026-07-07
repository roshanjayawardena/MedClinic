using Appointments.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Appointments.Features.GetAppointmentById;

internal static class GetAppointmentByIdEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/appointments/{id:guid}", Handle)
            .WithName("GetAppointmentById")
            .WithTags("Appointments")
            .WithSummary("Get an appointment by ID")
            .Produces<GetAppointmentByIdResponse>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetAppointmentByIdQuery(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}
