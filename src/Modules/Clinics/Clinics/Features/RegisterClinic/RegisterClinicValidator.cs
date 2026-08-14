using Clinics.Contracts;
using FluentValidation;

namespace Clinics.Features.RegisterClinic;

public sealed class RegisterClinicValidator : AbstractValidator<RegisterClinicCommand>
{
    private static readonly string[] ValidPlans = ["Free", "Standard", "Enterprise"];

    public RegisterClinicValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100)
            .Matches(@"^[a-z0-9-]+$")
            .WithMessage("Slug must contain only lowercase letters, digits, and hyphens.");
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress().MaximumLength(300);
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Plan).NotEmpty().Must(p => ValidPlans.Contains(p))
            .WithMessage("Plan must be Free, Standard, or Enterprise.");
    }
}
