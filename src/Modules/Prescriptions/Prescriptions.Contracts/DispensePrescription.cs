using Core;
using Mediator;

namespace Prescriptions.Contracts;

public sealed record DispensePrescriptionCommand(Guid PrescriptionId) : IRequest<Result<DispensePrescriptionResponse>>;

public sealed record DispensePrescriptionResponse(Guid PrescriptionId, string Status, DateTimeOffset DispensedAt);
