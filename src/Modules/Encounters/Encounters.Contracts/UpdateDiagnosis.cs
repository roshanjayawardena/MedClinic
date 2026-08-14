using Core;
using Mediator;

namespace Encounters.Contracts;

public sealed record UpdateDiagnosisCommand(
    Guid EncounterId,
    string Icd10Code,
    string Description,
    string DiagnosisType) : IRequest<Result<UpdateDiagnosisResponse>>;

public sealed record UpdateDiagnosisResponse(Guid EncounterId);
