using Core;
using Mediator;

namespace Patients.Contracts;

public sealed record GetPatientAllergiesQuery(Guid PatientId) : IRequest<Result<GetPatientAllergiesResponse>>;

public sealed record GetPatientAllergiesResponse(IReadOnlyList<PatientAllergyDto> Allergies);

public sealed record PatientAllergyDto(string DrugName, string Severity);
