using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Prescriptions.Contracts;
using Prescriptions.Domain;
using Prescriptions.Persistence;

namespace Prescriptions.Features.GetPrescriptionById;

public sealed class GetPrescriptionByIdHandler(
    IDbContextFactory<PrescriptionsDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<GetPrescriptionByIdQuery, Result<GetPrescriptionByIdResponse>>
{
    public async ValueTask<Result<GetPrescriptionByIdResponse>> Handle(
        GetPrescriptionByIdQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var prescription = await db.Prescriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == query.PrescriptionId, cancellationToken)
            .ConfigureAwait(false);

        if (prescription is null)
            return Result<GetPrescriptionByIdResponse>.Fail(
                new Error("Prescription.NotFound", $"Prescription {query.PrescriptionId} not found."));

        // Audit the read — golden rule 9: every Prescription read emits an audit entry.
        await using var auditDb = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        auditDb.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "PrescriptionRead",
            EntityType: nameof(Prescription),
            EntityId: prescription.Id.ToString(),
            PerformedBy: null,
            timeProvider.GetUtcNow()));
        await auditDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<GetPrescriptionByIdResponse>.Ok(new GetPrescriptionByIdResponse(
            prescription.Id,
            prescription.EncounterId,
            prescription.PatientId,
            prescription.Status.ToString(),
            prescription.DosageInstructions,
            prescription.QuantityDays,
            prescription.ActivatedAt,
            prescription.DispensedAt,
            prescription.CreatedAt));
    }
}
