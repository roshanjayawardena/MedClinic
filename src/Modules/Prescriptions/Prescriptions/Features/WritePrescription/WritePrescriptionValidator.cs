using FluentValidation;
using Prescriptions.Contracts;

namespace Prescriptions.Features.WritePrescription;

public sealed class WritePrescriptionValidator : AbstractValidator<WritePrescriptionCommand>
{
    public WritePrescriptionValidator()
    {
        RuleFor(x => x.EncounterId).NotEmpty();
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.DrugName).NotEmpty().MaximumLength(300);
        RuleFor(x => x.DosageInstructions).NotEmpty().MaximumLength(500);
        RuleFor(x => x.QuantityDays).GreaterThan(0).LessThanOrEqualTo(365);
    }
}
