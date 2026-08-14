using Core;
using Mediator;

namespace Clinics.Contracts;

public sealed record RegisterClinicCommand(
    string Name,
    string Slug,
    string ContactEmail,
    string TimeZoneId,
    string Plan)
    : IRequest<Result<RegisterClinicResponse>>;

public sealed record RegisterClinicResponse(
    Guid ClinicId,
    string Name,
    string Slug,
    string Plan);
