using Appointments.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Appointments.Features.ListAppointments;

internal static class ListAppointmentsEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/appointments", Handle)
            .WithName("ListAppointments")
            .WithTags("Appointments")
            .WithSummary("List appointments with pagination")
            .Produces<ListAppointmentsResponse>();
    }

    private static async Task<IResult> Handle(
        int page,
        int pageSize,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListAppointmentsQuery(page, pageSize), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem("Failed to list appointments.", statusCode: 500);
    }
}
