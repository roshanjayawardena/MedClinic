using Core;
using Mediator;

namespace Prescriptions.Contracts;

public sealed record WritePrescriptionCommand(
    Guid EncounterId,
    Guid PatientId,
    string DrugName,
    string DosageInstructions,
    int QuantityDays) : IRequest<Result<WritePrescriptionResponse>>;

public sealed record WritePrescriptionResponse(Guid PrescriptionId);
