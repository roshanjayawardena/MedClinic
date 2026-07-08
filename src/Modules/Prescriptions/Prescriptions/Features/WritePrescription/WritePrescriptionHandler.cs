using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;
using Prescriptions.Contracts;
using Prescriptions.Domain;
using Prescriptions.Persistence;

namespace Prescriptions.Features.WritePrescription;

public sealed class WritePrescriptionHandler(
    IDbContextFactory<PrescriptionsDbContext> dbFactory,
    IMediator mediator,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<WritePrescriptionCommand, Result<WritePrescriptionResponse>>
{
    public async ValueTask<Result<WritePrescriptionResponse>> Handle(
        WritePrescriptionCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Rule 1: prescription requires a closed encounter (enforced via local read model).
        var encounterClosed = await db.ClosedEncounters
            .AnyAsync(r => r.EncounterId == command.EncounterId
                        && r.PatientId == command.PatientId, cancellationToken)
            .ConfigureAwait(false);

        if (!encounterClosed)
            return Result<WritePrescriptionResponse>.Fail(new Error(
                "Prescription.NoClosedEncounter",
                $"Encounter {command.EncounterId} is not closed or does not belong to patient {command.PatientId}."));

        // Rule 2: allergy conflict check via cross-module query (Patients.Contracts only).
        var allergiesResult = await mediator
            .Send(new GetPatientAllergiesQuery(command.PatientId), cancellationToken)
            .ConfigureAwait(false);

        if (!allergiesResult.IsSuccess)
            return Result<WritePrescriptionResponse>.Fail(allergiesResult.Error!);

        var conflict = allergiesResult.Value.Allergies
            .FirstOrDefault(a => a.DrugName.Contains(command.DrugName, StringComparison.OrdinalIgnoreCase)
                               || command.DrugName.Contains(a.DrugName, StringComparison.OrdinalIgnoreCase));

        if (conflict is not null)
            return Result<WritePrescriptionResponse>.Fail(new Error(
                "Prescription.AllergyConflict",
                $"Patient has a recorded {conflict.Severity} allergy that conflicts with the prescribed drug."));

        var prescription = Prescription.Write(
            command.EncounterId,
            command.PatientId,
            command.DrugName,
            command.DosageInstructions,
            command.QuantityDays);

        db.Prescriptions.Add(prescription);
        db.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "PrescriptionWritten",
            EntityType: nameof(Prescription),
            EntityId: prescription.Id.ToString(),
            PerformedBy: null,
            timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Log using only the surrogate ID — DrugName is PHI and must never appear in logs.
        return Result<WritePrescriptionResponse>.Ok(new WritePrescriptionResponse(prescription.Id));
    }
}
