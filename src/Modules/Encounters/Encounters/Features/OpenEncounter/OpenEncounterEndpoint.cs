using Encounters.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Encounters.Features.OpenEncounter;

internal static class OpenEncounterEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/encounters", Handle)
            .WithName("OpenEncounter")
            .WithTags("Encounters")
            .WithSummary("Open a clinical encounter for a checked-in appointment")
            .AddEndpointFilter<ValidationFilter<OpenEncounterCommand>>()
            .Produces<OpenEncounterResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        OpenEncounterCommand command,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/encounters/{result.Value.EncounterId}", result.Value)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.Error!.Code] = [result.Error.Message],
            });
    }
}
