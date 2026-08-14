using Core;
using Encounters.Contracts;
using Encounters.Domain;
using Encounters.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Encounters.Features.UpdateDiagnosis;

public sealed class UpdateDiagnosisHandler(
    IDbContextFactory<EncountersDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateDiagnosisCommand, Result<UpdateDiagnosisResponse>>
{
    public async ValueTask<Result<UpdateDiagnosisResponse>> Handle(
        UpdateDiagnosisCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var encounter = await db.Encounters
            .Include(e => e.Diagnoses)
            .FirstOrDefaultAsync(e => e.Id == command.EncounterId, cancellationToken)
            .ConfigureAwait(false);

        if (encounter is null)
            return Result<UpdateDiagnosisResponse>.Fail(
                new Error("Encounter.NotFound", $"Encounter {command.EncounterId} not found."));

        if (!Enum.TryParse<DiagnosisType>(command.DiagnosisType, ignoreCase: true, out var diagnosisType))
            return Result<UpdateDiagnosisResponse>.Fail(
                new Error("Diagnosis.InvalidType",
                    $"'{command.DiagnosisType}' is not a valid diagnosis type."));

        var result = encounter.UpdateDiagnosis(command.Icd10Code, command.Description, diagnosisType);
        if (result.IsFailure)
            return Result<UpdateDiagnosisResponse>.Fail(result.Error!);

        db.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "DiagnosisUpdated",
            EntityType: nameof(Encounter),
            EntityId: encounter.Id.ToString(),
            PerformedBy: null,
            timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<UpdateDiagnosisResponse>.Ok(new UpdateDiagnosisResponse(encounter.Id));
    }
}
