using Core;
using Mediator;

namespace Encounters.Contracts;

public sealed record RemoveDiagnosisCommand(
    Guid EncounterId,
    string Icd10Code) : IRequest<Result<RemoveDiagnosisResponse>>;

public sealed record RemoveDiagnosisResponse(Guid EncounterId, int DiagnosisCount);
