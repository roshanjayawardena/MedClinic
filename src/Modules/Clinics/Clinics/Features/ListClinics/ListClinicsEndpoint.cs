using Clinics.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Clinics.Features.ListClinics;

public static class ListClinicsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/clinics", async (
            IMediator mediator,
            HttpContext ctx,
            int page = 1,
            int pageSize = 20,
            bool activeOnly = true) =>
        {
            var result = await mediator.Send(new ListClinicsQuery(page, pageSize, activeOnly));
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblemResult(ctx);
        })
        .RequireAuthorization("SystemAdmin")
        .WithTags("Clinics")
        .WithSummary("List all clinics");
}
