using Mediator;

namespace Patients.Contracts;

public sealed record RegisterPatientCommand(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string ContactPhone,
    bool ConsentToDataProcessing) : IRequest<Core.Result<RegisterPatientResponse>>;

public sealed record RegisterPatientResponse(Guid PatientId);
