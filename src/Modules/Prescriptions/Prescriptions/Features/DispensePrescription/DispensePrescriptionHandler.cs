using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Prescriptions.Contracts;
using Prescriptions.Contracts.Events;
using Prescriptions.Domain;
using Prescriptions.Persistence;

namespace Prescriptions.Features.DispensePrescription;

public sealed class DispensePrescriptionHandler(
    IDbContextFactory<PrescriptionsDbContext> dbFactory,
    IMediator mediator,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<DispensePrescriptionCommand, Result<DispensePrescriptionResponse>>
{
    public async ValueTask<Result<DispensePrescriptionResponse>> Handle(
        DispensePrescriptionCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var prescription = await db.Prescriptions
            .FirstOrDefaultAsync(p => p.Id == command.PrescriptionId, cancellationToken)
            .ConfigureAwait(false);

        if (prescription is null)
            return Result<DispensePrescriptionResponse>.Fail(
                new Error("Prescription.NotFound", $"Prescription {command.PrescriptionId} not found."));

        var result = prescription.Dispense(timeProvider.GetUtcNow());
        if (!result.IsSuccess)
            return Result<DispensePrescriptionResponse>.Fail(result.Error!);

        db.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "PrescriptionDispensed",
            EntityType: nameof(Prescription),
            EntityId: prescription.Id.ToString(),
            PerformedBy: null,
            timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Publish integration event after commit — Billing subscribes to create an invoice.
        // ClinicId = TenantId; DrugName excluded intentionally (PHI).
        await mediator.Publish(
            new PrescriptionDispensedIntegrationEvent(
                prescription.Id,
                prescription.EncounterId,
                prescription.PatientId,
                tenantContext.TenantId,
                prescription.DispensedAt!.Value,
                timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);

        return Result<DispensePrescriptionResponse>.Ok(
            new DispensePrescriptionResponse(
                prescription.Id,
                prescription.Status.ToString(),
                prescription.DispensedAt!.Value));
    }
}
