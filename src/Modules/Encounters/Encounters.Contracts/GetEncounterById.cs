using Core;
using Mediator;

namespace Encounters.Contracts;

public sealed record GetEncounterByIdQuery(Guid EncounterId) : IRequest<Result<GetEncounterByIdResponse>>;

public sealed record GetEncounterByIdResponse(
    Guid EncounterId,
    Guid AppointmentId,
    Guid PatientId,
    string Status,
    string? ClinicalNotes,
    IReadOnlyList<DiagnosisDto> Diagnoses,
    VitalSignsDto? Vitals,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);

public sealed record DiagnosisDto(string Icd10Code, string Description, string Type);

public sealed record VitalSignsDto(
    int? SystolicBp,
    int? DiastolicBp,
    int? HeartRateBpm,
    decimal? TemperatureCelsius,
    int? RespiratoryRatePerMin,
    int? OxygenSaturationPercent,
    decimal? WeightKg);
