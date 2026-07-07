using Appointments.Contracts;
using Appointments.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Appointments.Features.CheckInAppointment;

public sealed class CheckInAppointmentHandler(
    IDbContextFactory<AppointmentsDbContext> dbFactory,
    TimeProvider timeProvider)
    : IRequestHandler<CheckInAppointmentCommand, Result<CheckInAppointmentResponse>>
{
    public async ValueTask<Result<CheckInAppointmentResponse>> Handle(
        CheckInAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, cancellationToken)
            .ConfigureAwait(false);

        if (appointment is null)
            return Result<CheckInAppointmentResponse>.Fail(
                new Error("Appointment.NotFound", $"Appointment {command.AppointmentId} not found."));

        var result = appointment.CheckIn(timeProvider.GetUtcNow());
        if (result.IsFailure)
            return Result<CheckInAppointmentResponse>.Fail(result.Error!);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CheckInAppointmentResponse>.Ok(
            new CheckInAppointmentResponse(appointment.Id, appointment.Status.ToString()));
    }
}
