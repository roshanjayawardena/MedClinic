using Encounters.Contracts;
using FluentValidation;

namespace Encounters.Features.OpenEncounter;

public sealed class OpenEncounterValidator : AbstractValidator<OpenEncounterCommand>
{
    public OpenEncounterValidator()
    {
        RuleFor(c => c.AppointmentId).NotEmpty();
        RuleFor(c => c.PatientId).NotEmpty();
        RuleFor(c => c.ClinicalNotes).MaximumLength(4000).When(c => c.ClinicalNotes is not null);
    }
}
