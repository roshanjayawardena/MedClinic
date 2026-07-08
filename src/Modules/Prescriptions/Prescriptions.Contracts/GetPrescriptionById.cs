using Core;
using Mediator;

namespace Prescriptions.Contracts;

public sealed record GetPrescriptionByIdQuery(Guid PrescriptionId) : IRequest<Result<GetPrescriptionByIdResponse>>;

public sealed record GetPrescriptionByIdResponse(
    Guid PrescriptionId,
    Guid EncounterId,
    Guid PatientId,
    string Status,
    string DosageInstructions,
    int QuantityDays,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DispensedAt,
    DateTimeOffset CreatedAt);
