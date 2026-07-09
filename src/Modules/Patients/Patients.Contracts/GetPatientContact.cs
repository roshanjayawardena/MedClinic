using Core;
using Mediator;

namespace Patients.Contracts;

/// <summary>
/// Minimal cross-module query — returns only the fields needed for outreach decisions.
/// Does NOT return PHI fields (name, DOB) that callers do not need.
/// </summary>
public sealed record GetPatientContactQuery(Guid PatientId) : IRequest<Result<GetPatientContactResponse>>;

/// <summary>
/// ContactPhone is PHI. Callers must use it immediately and never log it.
/// </summary>
public sealed record GetPatientContactResponse(
    Guid PatientId,
    string ContactPhone,
    bool ConsentToCommunications);
