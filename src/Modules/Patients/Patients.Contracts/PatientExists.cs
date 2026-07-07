using Core;
using Mediator;

namespace Patients.Contracts;

/// <summary>
/// Cross-module query — Appointments and other modules use this via Contracts
/// to verify a patient exists without taking a runtime dependency on Patients.
/// </summary>
public sealed record PatientExistsQuery(Guid PatientId) : IRequest<Result<bool>>;
