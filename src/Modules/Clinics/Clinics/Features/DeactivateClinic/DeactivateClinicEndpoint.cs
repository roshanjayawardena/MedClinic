using Clinics.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Clinics.Features.DeactivateClinic;

public static class DeactivateClinicEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/clinics/{id:guid}/deactivate", async (Guid id, IMediator mediator, HttpContext ctx) =>
        {
            var result = await mediator.Send(new DeactivateClinicCommand(id));
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblemResult(ctx);
        })
        .RequireAuthorization("SystemAdmin")
        .WithTags("Clinics")
        .WithSummary("Deactivate a clinic (blocks all logins for that tenant)");
}
