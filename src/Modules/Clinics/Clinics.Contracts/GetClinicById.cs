using Core;
using Mediator;

namespace Clinics.Contracts;

public sealed record GetClinicByIdQuery(Guid ClinicId) : IRequest<Result<GetClinicByIdResponse>>;

public sealed record GetClinicByIdResponse(
    Guid ClinicId,
    string Name,
    string Slug,
    string ContactEmail,
    string TimeZoneId,
    string Plan,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeactivatedAt);
