using Core;
using Encounters.Contracts;
using Encounters.Domain;
using Encounters.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Encounters.Features.AddDiagnosis;

public sealed class AddDiagnosisHandler(
    IDbContextFactory<EncountersDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<AddDiagnosisCommand, Result<AddDiagnosisResponse>>
{
    public async ValueTask<Result<AddDiagnosisResponse>> Handle(
        AddDiagnosisCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var encounter = await db.Encounters
            .Include(e => e.Diagnoses)
            .FirstOrDefaultAsync(e => e.Id == command.EncounterId, cancellationToken)
            .ConfigureAwait(false);

        if (encounter is null)
            return Result<AddDiagnosisResponse>.Fail(
                new Error("Encounter.NotFound", $"Encounter {command.EncounterId} not found."));

        if (!Enum.TryParse<DiagnosisType>(command.DiagnosisType, ignoreCase: true, out var diagnosisType))
            return Result<AddDiagnosisResponse>.Fail(
                new Error("Diagnosis.InvalidType",
                    $"'{command.DiagnosisType}' is not a valid diagnosis type. Use Primary, Secondary, or Comorbidity."));

        var addResult = encounter.AddDiagnosis(command.Icd10Code, command.Description, diagnosisType);
        if (addResult.IsFailure)
            return Result<AddDiagnosisResponse>.Fail(addResult.Error!);

        db.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "DiagnosisAdded",
            EntityType: nameof(Encounter),
            EntityId: encounter.Id.ToString(),
            PerformedBy: null,
            timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<AddDiagnosisResponse>.Ok(
            new AddDiagnosisResponse(encounter.Id, encounter.Diagnoses.Count));
    }
}
