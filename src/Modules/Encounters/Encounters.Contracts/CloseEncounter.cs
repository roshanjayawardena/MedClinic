using Core;
using Mediator;

namespace Encounters.Contracts;

public sealed record CloseEncounterCommand(
    Guid EncounterId,
    string? ClinicalNotes = null) : IRequest<Result<CloseEncounterResponse>>;

public sealed record CloseEncounterResponse(Guid EncounterId);
