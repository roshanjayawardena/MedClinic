using Core;
using Mediator;

namespace Clinics.Contracts;

public sealed record DeactivateClinicCommand(Guid ClinicId) : IRequest<Result<DeactivateClinicResponse>>;

public sealed record DeactivateClinicResponse(Guid ClinicId, DateTimeOffset DeactivatedAt);
