using Core;
using Encounters.Contracts;
using Encounters.Domain;
using Encounters.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Encounters.Features.RemoveDiagnosis;

public sealed class RemoveDiagnosisHandler(
    IDbContextFactory<EncountersDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<RemoveDiagnosisCommand, Result<RemoveDiagnosisResponse>>
{
    public async ValueTask<Result<RemoveDiagnosisResponse>> Handle(
        RemoveDiagnosisCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var encounter = await db.Encounters
            .Include(e => e.Diagnoses)
            .FirstOrDefaultAsync(e => e.Id == command.EncounterId, cancellationToken)
            .ConfigureAwait(false);

        if (encounter is null)
            return Result<RemoveDiagnosisResponse>.Fail(
                new Error("Encounter.NotFound", $"Encounter {command.EncounterId} not found."));

        var result = encounter.RemoveDiagnosis(command.Icd10Code);
        if (result.IsFailure)
            return Result<RemoveDiagnosisResponse>.Fail(result.Error!);

        db.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "DiagnosisRemoved",
            EntityType: nameof(Encounter),
            EntityId: encounter.Id.ToString(),
            PerformedBy: null,
            timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<RemoveDiagnosisResponse>.Ok(
            new RemoveDiagnosisResponse(encounter.Id, encounter.Diagnoses.Count));
    }
}
