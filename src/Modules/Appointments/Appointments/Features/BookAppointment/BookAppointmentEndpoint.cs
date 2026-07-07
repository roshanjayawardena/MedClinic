using Appointments.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Appointments.Features.BookAppointment;

internal static class BookAppointmentEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/appointments", Handle)
            .WithName("BookAppointment")
            .WithTags("Appointments")
            .WithSummary("Book a new appointment for a patient")
            .AddEndpointFilter<ValidationFilter<BookAppointmentCommand>>()
            .Produces<BookAppointmentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        BookAppointmentCommand command,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/appointments/{result.Value.AppointmentId}", result.Value)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.Error!.Code] = [result.Error.Message],
            });
    }
}
