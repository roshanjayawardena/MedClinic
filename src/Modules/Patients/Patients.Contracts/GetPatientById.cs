using Core;
using Mediator;

namespace Patients.Contracts;

public sealed record GetPatientByIdQuery(Guid PatientId) : IRequest<Result<GetPatientByIdResponse>>;

public sealed record GetPatientByIdResponse(
    Guid PatientId,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string ContactPhone,
    bool ConsentToCommunications);
