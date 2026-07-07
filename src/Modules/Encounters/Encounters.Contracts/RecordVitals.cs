using Core;
using Mediator;

namespace Encounters.Contracts;

public sealed record RecordVitalsCommand(
    Guid EncounterId,
    int? SystolicBp,
    int? DiastolicBp,
    int? HeartRateBpm,
    decimal? TemperatureCelsius,
    int? RespiratoryRatePerMin,
    int? OxygenSaturationPercent,
    decimal? WeightKg) : IRequest<Result<RecordVitalsResponse>>;

public sealed record RecordVitalsResponse(Guid EncounterId);
