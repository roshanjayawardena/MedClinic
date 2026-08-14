using Core;
using Mediator;

namespace Encounters.Contracts;

public sealed record ListEncountersQuery(int Page, int PageSize)
    : IRequest<Result<ListEncountersResponse>>;

public sealed record ListEncountersResponse(
    IReadOnlyList<EncounterListItemDto> Items,
    int TotalCount);

public sealed record EncounterListItemDto(
    Guid EncounterId,
    Guid PatientId,
    string PatientFirstName,
    string PatientLastName,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);
