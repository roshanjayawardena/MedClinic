using FluentValidation;
using Patients.Contracts;

namespace Patients.Features.RegisterPatient;

public sealed class RegisterPatientValidator : AbstractValidator<RegisterPatientCommand>
{
    public RegisterPatientValidator(TimeProvider timeProvider)
    {
        RuleFor(c => c.FirstName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.LastName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ContactPhone).NotEmpty().MaximumLength(50);

        RuleFor(c => c.DateOfBirth)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            .WithMessage("Date of birth cannot be in the future.");

        RuleFor(c => c.ConsentToDataProcessing)
            .Equal(true)
            .WithMessage("Consent to data processing is required to register a patient.");
    }
}
