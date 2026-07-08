using Encounters.Contracts.Events;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Prescriptions.Domain;
using Prescriptions.Persistence;

namespace Prescriptions.Features.OnEncounterClosed;

/// <summary>
/// Subscribes to EncounterClosedIntegrationEvent and persists a local read model
/// so WritePrescription can verify encounter closure without a cross-module query.
/// </summary>
public sealed class OnEncounterClosedHandler(IDbContextFactory<PrescriptionsDbContext> dbFactory)
    : INotificationHandler<EncounterClosedIntegrationEvent>
{
    public async ValueTask Handle(
        EncounterClosedIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Idempotency guard — integration events may be delivered more than once.
        var alreadyRecorded = await db.ClosedEncounters
            .AnyAsync(r => r.EncounterId == notification.EncounterId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyRecorded)
            return;

        db.ClosedEncounters.Add(new ClosedEncounterRecord
        {
            EncounterId = notification.EncounterId,
            PatientId = notification.PatientId,
            TenantId = notification.ClinicId,
            ClosedAt = notification.OccurredAt,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
