using Appointments.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Appointments.Features.CancelAppointment;

internal static class CancelAppointmentEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/appointments/{id:guid}/cancel", Handle)
            .WithName("CancelAppointment")
            .WithTags("Appointments")
            .WithSummary("Cancel a scheduled or checked-in appointment")
            .Produces<CancelAppointmentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        Guid id,
        CancelAppointmentRequest body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CancelAppointmentCommand(id, body.Reason), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error!.Code == "Appointment.NotFound"
                ? TypedResults.NotFound()
                : TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [result.Error.Code] = [result.Error.Message],
                });
    }
}

internal sealed record CancelAppointmentRequest(string Reason);
