using Core;
using Encounters.Contracts;
using Encounters.Domain;
using Encounters.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Encounters.Features.RecordVitals;

public sealed class RecordVitalsHandler(
    IDbContextFactory<EncountersDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<RecordVitalsCommand, Result<RecordVitalsResponse>>
{
    public async ValueTask<Result<RecordVitalsResponse>> Handle(
        RecordVitalsCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var encounter = await db.Encounters
            .FirstOrDefaultAsync(e => e.Id == command.EncounterId, cancellationToken)
            .ConfigureAwait(false);

        if (encounter is null)
            return Result<RecordVitalsResponse>.Fail(
                new Error("Encounter.NotFound", $"Encounter {command.EncounterId} not found."));

        var vitals = new VitalSigns(
            command.SystolicBp,
            command.DiastolicBp,
            command.HeartRateBpm,
            command.TemperatureCelsius,
            command.RespiratoryRatePerMin,
            command.OxygenSaturationPercent,
            command.WeightKg);

        var recordResult = encounter.RecordVitals(vitals);
        if (recordResult.IsFailure)
            return Result<RecordVitalsResponse>.Fail(recordResult.Error!);

        db.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "VitalsRecorded",
            EntityType: nameof(Encounter),
            EntityId: encounter.Id.ToString(),
            PerformedBy: null,
            timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<RecordVitalsResponse>.Ok(new RecordVitalsResponse(encounter.Id));
    }
}
