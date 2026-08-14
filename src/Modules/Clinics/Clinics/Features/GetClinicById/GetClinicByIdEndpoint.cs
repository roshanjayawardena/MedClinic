using Clinics.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Clinics.Features.GetClinicById;

public static class GetClinicByIdEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/clinics/{id:guid}", async (Guid id, IMediator mediator, HttpContext ctx) =>
        {
            var result = await mediator.Send(new GetClinicByIdQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblemResult(ctx);
        })
        .RequireAuthorization("SystemAdmin")
        .WithTags("Clinics")
        .WithSummary("Get clinic by ID");
}
