using Appointments.Contracts;
using FluentValidation;

namespace Appointments.Features.BookAppointment;

public sealed class BookAppointmentValidator : AbstractValidator<BookAppointmentCommand>
{
    public BookAppointmentValidator(TimeProvider timeProvider)
    {
        RuleFor(c => c.PatientId).NotEmpty();

        RuleFor(c => c.ScheduledAt)
            .GreaterThan(_ => timeProvider.GetUtcNow())
            .WithMessage("Appointment must be scheduled in the future.");

        RuleFor(c => c.DurationMinutes)
            .InclusiveBetween(10, 120)
            .WithMessage("Duration must be between 10 and 120 minutes.");

        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
    }
}
