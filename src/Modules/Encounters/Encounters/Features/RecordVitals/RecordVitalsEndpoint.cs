using Encounters.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Encounters.Features.RecordVitals;

internal static class RecordVitalsEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/encounters/{id:guid}/vitals", Handle)
            .WithName("RecordVitals")
            .WithTags("Encounters")
            .WithSummary("Record vital signs for an open encounter")
            .AddEndpointFilter<ValidationFilter<RecordVitalsCommand>>()
            .Produces<RecordVitalsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        Guid id,
        RecordVitalsRequest body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new RecordVitalsCommand(
            id,
            body.SystolicBp,
            body.DiastolicBp,
            body.HeartRateBpm,
            body.TemperatureCelsius,
            body.RespiratoryRatePerMin,
            body.OxygenSaturationPercent,
            body.WeightKg);

        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error!.Code == "Encounter.NotFound"
                ? TypedResults.NotFound()
                : TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [result.Error.Code] = [result.Error.Message],
                });
    }
}

internal sealed record RecordVitalsRequest(
    int? SystolicBp,
    int? DiastolicBp,
    int? HeartRateBpm,
    decimal? TemperatureCelsius,
    int? RespiratoryRatePerMin,
    int? OxygenSaturationPercent,
    decimal? WeightKg);
