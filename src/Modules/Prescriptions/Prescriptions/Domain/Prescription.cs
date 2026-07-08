using Core;

namespace Prescriptions.Domain;

public sealed class Prescription : AuditableEntity
{
    private Prescription() { }

    public Guid EncounterId { get; private set; }
    public Guid PatientId { get; private set; }

    // DrugName is PHI — never logged. Stored encrypted in production; plaintext here for reference simplicity.
    public string DrugName { get; private set; } = string.Empty;
    public string DosageInstructions { get; private set; } = string.Empty;
    public int QuantityDays { get; private set; }
    public PrescriptionStatus Status { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset? DispensedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public static Prescription Write(
        Guid encounterId,
        Guid patientId,
        string drugName,
        string dosageInstructions,
        int quantityDays) =>
        new()
        {
            Id = Guid.NewGuid(),
            EncounterId = encounterId,
            PatientId = patientId,
            DrugName = drugName,
            DosageInstructions = dosageInstructions,
            QuantityDays = quantityDays,
            Status = PrescriptionStatus.Draft,
        };

    public Result Activate(DateTimeOffset now)
    {
        if (Status != PrescriptionStatus.Draft)
            return Result.Fail(new Error("Prescription.InvalidStatus",
                $"Only a Draft prescription can be activated. Current status: {Status}."));

        Status = PrescriptionStatus.Active;
        ActivatedAt = now;
        return Result.Ok();
    }

    public Result Dispense(DateTimeOffset now)
    {
        if (Status != PrescriptionStatus.Active)
            return Result.Fail(new Error("Prescription.InvalidStatus",
                $"Only an Active prescription can be dispensed. Current status: {Status}."));

        Status = PrescriptionStatus.Dispensed;
        DispensedAt = now;
        return Result.Ok();
    }

    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (Status is PrescriptionStatus.Dispensed or PrescriptionStatus.Cancelled)
            return Result.Fail(new Error("Prescription.InvalidStatus",
                $"Cannot cancel a prescription with status {Status}."));

        Status = PrescriptionStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = now;
        return Result.Ok();
    }
}
