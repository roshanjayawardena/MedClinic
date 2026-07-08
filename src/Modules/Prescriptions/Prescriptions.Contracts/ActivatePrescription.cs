using Core;
using Mediator;

namespace Prescriptions.Contracts;

public sealed record ActivatePrescriptionCommand(Guid PrescriptionId) : IRequest<Result<ActivatePrescriptionResponse>>;

public sealed record ActivatePrescriptionResponse(Guid PrescriptionId, string Status);
