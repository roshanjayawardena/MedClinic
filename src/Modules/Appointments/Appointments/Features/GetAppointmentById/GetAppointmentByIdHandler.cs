using Appointments.Contracts;
using Appointments.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Appointments.Features.GetAppointmentById;

public sealed class GetAppointmentByIdHandler(IDbContextFactory<AppointmentsDbContext> dbFactory)
    : IRequestHandler<GetAppointmentByIdQuery, Result<GetAppointmentByIdResponse>>
{
    public async ValueTask<Result<GetAppointmentByIdResponse>> Handle(
        GetAppointmentByIdQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var appointment = await db.Appointments
            .AsNoTracking()
            .Where(a => a.Id == query.AppointmentId)
            .Select(a => new GetAppointmentByIdResponse(
                a.Id,
                a.PatientId,
                a.ScheduledAt,
                a.DurationMinutes,
                a.Reason,
                a.Status.ToString(),
                a.CheckedInAt,
                a.CompletedAt,
                a.CancelledAt))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return appointment is null
            ? Result<GetAppointmentByIdResponse>.Fail(
                new Error("Appointment.NotFound", $"Appointment {query.AppointmentId} not found."))
            : Result<GetAppointmentByIdResponse>.Ok(appointment);
    }
}
