using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Prescriptions.Contracts;
using Prescriptions.Domain;
using Prescriptions.Persistence;

namespace Prescriptions.Features.ActivatePrescription;

public sealed class ActivatePrescriptionHandler(
    IDbContextFactory<PrescriptionsDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<ActivatePrescriptionCommand, Result<ActivatePrescriptionResponse>>
{
    public async ValueTask<Result<ActivatePrescriptionResponse>> Handle(
        ActivatePrescriptionCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var prescription = await db.Prescriptions
            .FirstOrDefaultAsync(p => p.Id == command.PrescriptionId, cancellationToken)
            .ConfigureAwait(false);

        if (prescription is null)
            return Result<ActivatePrescriptionResponse>.Fail(
                new Error("Prescription.NotFound", $"Prescription {command.PrescriptionId} not found."));

        var result = prescription.Activate(timeProvider.GetUtcNow());
        if (!result.IsSuccess)
            return Result<ActivatePrescriptionResponse>.Fail(result.Error!);

        db.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "PrescriptionActivated",
            EntityType: nameof(Prescription),
            EntityId: prescription.Id.ToString(),
            PerformedBy: null,
            timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ActivatePrescriptionResponse>.Ok(
            new ActivatePrescriptionResponse(prescription.Id, prescription.Status.ToString()));
    }
}
