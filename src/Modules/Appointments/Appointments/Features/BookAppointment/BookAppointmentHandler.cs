using Appointments.Contracts;
using Appointments.Contracts.Events;
using Appointments.Domain;
using Appointments.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;

namespace Appointments.Features.BookAppointment;

public sealed class BookAppointmentHandler(
    IDbContextFactory<AppointmentsDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    IMediator mediator)
    : IRequestHandler<BookAppointmentCommand, Result<BookAppointmentResponse>>
{
    public async ValueTask<Result<BookAppointmentResponse>> Handle(
        BookAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        // Cross-module query: verify the patient exists in this clinic.
        // We reference only Patients.Contracts — never the Patients runtime project.
        var patientResult = await mediator.Send(
            new PatientExistsQuery(command.PatientId), cancellationToken);

        if (!patientResult.Value)
            return Result<BookAppointmentResponse>.Fail(
                new Error("Patient.NotFound", $"Patient {command.PatientId} does not exist in this clinic."));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Double-booking guard: one appointment at a time for this clinic.
        // The global query filter means this only checks the current tenant's appointments.
        var endTime = command.ScheduledAt.AddMinutes(command.DurationMinutes);

        var hasOverlap = await db.Appointments
            .AnyAsync(a =>
                a.Status != AppointmentStatus.Cancelled &&
                a.ScheduledAt < endTime &&
                a.ScheduledAt.AddMinutes(a.DurationMinutes) > command.ScheduledAt,
                cancellationToken)
            .ConfigureAwait(false);

        if (hasOverlap)
            return Result<BookAppointmentResponse>.Fail(
                new Error("Appointment.DoubleBooking",
                    $"The clinic already has an appointment between {command.ScheduledAt:g} and {endTime:g}."));

        var appointment = Appointment.Book(
            command.PatientId,
            command.ScheduledAt,
            command.DurationMinutes,
            command.Reason);

        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Integration event — published in-process via Mediator.
        // Notifications module subscribes to schedule a reminder.
        await mediator.Publish(
            new AppointmentBookedIntegrationEvent(
                appointment.Id,
                appointment.PatientId,
                tenantContext.TenantId,
                appointment.ScheduledAt,
                timeProvider.GetUtcNow()),
            cancellationToken)
            .ConfigureAwait(false);

        return Result<BookAppointmentResponse>.Ok(new BookAppointmentResponse(appointment.Id));
    }
}
