using Core;
using Mediator;

namespace Encounters.Contracts;

public sealed record ListEncountersByPatientQuery(Guid PatientId)
    : IRequest<Result<IReadOnlyList<EncounterSummaryDto>>>;

public sealed record EncounterSummaryDto(
    Guid EncounterId,
    Guid PatientId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);
