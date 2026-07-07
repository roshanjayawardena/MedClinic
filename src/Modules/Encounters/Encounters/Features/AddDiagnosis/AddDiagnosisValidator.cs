using Encounters.Contracts;
using FluentValidation;

namespace Encounters.Features.AddDiagnosis;

public sealed class AddDiagnosisValidator : AbstractValidator<AddDiagnosisCommand>
{
    public AddDiagnosisValidator()
    {
        RuleFor(c => c.EncounterId).NotEmpty();
        RuleFor(c => c.Icd10Code).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Description).NotEmpty().MaximumLength(500);
        RuleFor(c => c.DiagnosisType).NotEmpty();
    }
}
