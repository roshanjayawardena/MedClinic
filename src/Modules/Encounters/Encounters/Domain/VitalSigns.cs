namespace Encounters.Domain;

/// <summary>
/// Point-in-time vital signs recorded during an encounter.
/// Owned entity — columns live in the encounters table.
/// </summary>
public sealed record VitalSigns(
    int? SystolicBp,
    int? DiastolicBp,
    int? HeartRateBpm,
    decimal? TemperatureCelsius,
    int? RespiratoryRatePerMin,
    int? OxygenSaturationPercent,
    decimal? WeightKg);
