using Core;
using Mediator;

namespace Patients.Contracts;

public sealed record AddAllergyCommand(
    Guid PatientId,
    string DrugName,
    string Severity,
    string? Notes) : IRequest<Result<AddAllergyResponse>>;

public sealed record AddAllergyResponse(Guid AllergyId);
