using FluentValidation;
using Patients.Contracts;

namespace Patients.Features.AddAllergy;

public sealed class AddAllergyValidator : AbstractValidator<AddAllergyCommand>
{
    private static readonly string[] ValidSeverities = ["Mild", "Moderate", "Severe"];

    public AddAllergyValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.DrugName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Severity).Must(s => ValidSeverities.Contains(s))
            .WithMessage("Severity must be Mild, Moderate, or Severe.");
        RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
    }
}
