using Core;
using Mediator;

namespace Clinics.Contracts;

public sealed record ListClinicsQuery(int Page, int PageSize, bool ActiveOnly = true)
    : IRequest<Result<ListClinicsResponse>>;

public sealed record ListClinicsResponse(
    IReadOnlyList<ClinicSummaryDto> Items,
    int TotalCount);

public sealed record ClinicSummaryDto(
    Guid ClinicId,
    string Name,
    string Slug,
    string Plan,
    bool IsActive,
    DateTimeOffset CreatedAt);
