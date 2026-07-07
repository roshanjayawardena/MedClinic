using Appointments.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Appointments.Features.CheckInAppointment;

internal static class CheckInAppointmentEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/appointments/{id:guid}/check-in", Handle)
            .WithName("CheckInAppointment")
            .WithTags("Appointments")
            .WithSummary("Check in a patient for their scheduled appointment")
            .Produces<CheckInAppointmentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CheckInAppointmentCommand(id), cancellationToken);

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
