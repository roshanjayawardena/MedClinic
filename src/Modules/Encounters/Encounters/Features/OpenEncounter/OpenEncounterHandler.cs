using Core;
using Encounters.Contracts;
using Encounters.Domain;
using Encounters.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Encounters.Features.OpenEncounter;

public sealed class OpenEncounterHandler(
    IDbContextFactory<EncountersDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<OpenEncounterCommand, Result<OpenEncounterResponse>>
{
    public async ValueTask<Result<OpenEncounterResponse>> Handle(
        OpenEncounterCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var encounter = Encounter.Open(command.AppointmentId, command.PatientId, command.ClinicalNotes);

        db.Encounters.Add(encounter);

        // Audit entry written in the same SaveChangesAsync — same transaction guarantee.
        db.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "EncounterOpened",
            EntityType: nameof(Encounter),
            EntityId: encounter.Id.ToString(),
            PerformedBy: null,
            timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<OpenEncounterResponse>.Ok(new OpenEncounterResponse(encounter.Id));
    }
}
