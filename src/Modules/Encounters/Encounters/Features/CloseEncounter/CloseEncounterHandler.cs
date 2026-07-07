using Core;
using Encounters.Contracts;
using Encounters.Contracts.Events;
using Encounters.Domain;
using Encounters.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Encounters.Features.CloseEncounter;

public sealed class CloseEncounterHandler(
    IDbContextFactory<EncountersDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    IMediator mediator)
    : IRequestHandler<CloseEncounterCommand, Result<CloseEncounterResponse>>
{
    public async ValueTask<Result<CloseEncounterResponse>> Handle(
        CloseEncounterCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var encounter = await db.Encounters
            .FirstOrDefaultAsync(e => e.Id == command.EncounterId, cancellationToken)
            .ConfigureAwait(false);

        if (encounter is null)
            return Result<CloseEncounterResponse>.Fail(
                new Error("Encounter.NotFound", $"Encounter {command.EncounterId} not found."));

        var now = timeProvider.GetUtcNow();
        var closeResult = encounter.Close(command.ClinicalNotes, now);
        if (closeResult.IsFailure)
            return Result<CloseEncounterResponse>.Fail(closeResult.Error!);

        // Audit written in the same SaveChangesAsync — same transaction guarantee.
        db.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "EncounterClosed",
            EntityType: nameof(Encounter),
            EntityId: encounter.Id.ToString(),
            PerformedBy: null,
            now));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Integration event — Prescriptions subscribes to unlock script writing.
        await mediator.Publish(
            new EncounterClosedIntegrationEvent(
                encounter.Id,
                encounter.PatientId,
                encounter.AppointmentId,
                tenantContext.TenantId,
                now),
            cancellationToken)
            .ConfigureAwait(false);

        return Result<CloseEncounterResponse>.Ok(new CloseEncounterResponse(encounter.Id));
    }
}
