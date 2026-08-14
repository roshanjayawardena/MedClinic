using Clinics.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Clinics.Features.RegisterClinic;

public static class RegisterClinicEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/clinics", async (RegisterClinicCommand command, IMediator mediator, HttpContext ctx) =>
        {
            var result = await mediator.Send(command);
            return result.IsSuccess
                ? Results.Created($"/clinics/{result.Value.ClinicId}", result.Value)
                : result.Error!.ToProblemResult(ctx);
        })
        .AddEndpointFilter<ValidationFilter<RegisterClinicCommand>>()
        .AllowAnonymous()
        .WithTags("Clinics")
        .WithSummary("Register a new clinic");
}
