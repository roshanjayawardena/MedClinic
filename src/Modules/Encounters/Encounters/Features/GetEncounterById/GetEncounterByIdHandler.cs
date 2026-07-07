using Core;
using Encounters.Contracts;
using Encounters.Domain;
using Encounters.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Encounters.Features.GetEncounterById;

public sealed class GetEncounterByIdHandler(
    IDbContextFactory<EncountersDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<GetEncounterByIdQuery, Result<GetEncounterByIdResponse>>
{
    public async ValueTask<Result<GetEncounterByIdResponse>> Handle(
        GetEncounterByIdQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Include owned Diagnoses so they load with the encounter.
        var encounter = await db.Encounters
            .AsNoTracking()
            .Include(e => e.Diagnoses)
            .FirstOrDefaultAsync(e => e.Id == query.EncounterId, cancellationToken)
            .ConfigureAwait(false);

        if (encounter is null)
            return Result<GetEncounterByIdResponse>.Fail(
                new Error("Encounter.NotFound", $"Encounter {query.EncounterId} not found."));

        // Audit the read — golden rule 9: every Encounter read emits an audit entry.
        // Written in a separate SaveChangesAsync because the read used AsNoTracking.
        await using var auditDb = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        auditDb.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "EncounterRead",
            EntityType: nameof(Encounter),
            EntityId: encounter.Id.ToString(),
            PerformedBy: null,
            timeProvider.GetUtcNow()));
        await auditDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = new GetEncounterByIdResponse(
            encounter.Id,
            encounter.AppointmentId,
            encounter.PatientId,
            encounter.Status.ToString(),
            encounter.ClinicalNotes,
            encounter.Diagnoses.Select(d => new DiagnosisDto(d.Icd10Code, d.Description, d.Type.ToString())).ToList(),
            encounter.Vitals is null ? null : new VitalSignsDto(
                encounter.Vitals.SystolicBp,
                encounter.Vitals.DiastolicBp,
                encounter.Vitals.HeartRateBpm,
                encounter.Vitals.TemperatureCelsius,
                encounter.Vitals.RespiratoryRatePerMin,
                encounter.Vitals.OxygenSaturationPercent,
                encounter.Vitals.WeightKg),
            encounter.CreatedAt,
            encounter.ClosedAt);

        return Result<GetEncounterByIdResponse>.Ok(response);
    }
}
