using Core;
using Mediator;

namespace Encounters.Contracts;

public sealed record AddDiagnosisCommand(
    Guid EncounterId,
    string Icd10Code,
    string Description,
    string DiagnosisType) : IRequest<Result<AddDiagnosisResponse>>;

public sealed record AddDiagnosisResponse(Guid EncounterId, int DiagnosisCount);
