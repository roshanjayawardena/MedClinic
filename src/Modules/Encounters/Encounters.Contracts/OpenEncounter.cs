using Core;
using Mediator;

namespace Encounters.Contracts;

public sealed record OpenEncounterCommand(
    Guid AppointmentId,
    Guid PatientId,
    string? ClinicalNotes = null) : IRequest<Result<OpenEncounterResponse>>;

public sealed record OpenEncounterResponse(Guid EncounterId);
