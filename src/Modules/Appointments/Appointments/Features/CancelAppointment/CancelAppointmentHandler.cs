using Appointments.Contracts;
using Appointments.Contracts.Events;
using Appointments.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Appointments.Features.CancelAppointment;

public sealed class CancelAppointmentHandler(
    IDbContextFactory<AppointmentsDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    IMediator mediator,
    ClinicMetrics metrics)
    : IRequestHandler<CancelAppointmentCommand, Result<CancelAppointmentResponse>>
{
    public async ValueTask<Result<CancelAppointmentResponse>> Handle(
        CancelAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, cancellationToken)
            .ConfigureAwait(false);

        if (appointment is null)
            return Result<CancelAppointmentResponse>.Fail(
                new Error("Appointment.NotFound", $"Appointment {command.AppointmentId} not found."));

        var result = appointment.Cancel(command.Reason, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return Result<CancelAppointmentResponse>.Fail(result.Error!);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Integration event — Notifications module subscribes to delete the scheduled reminder job.
        await mediator.Publish(
            new AppointmentCancelledIntegrationEvent(
                appointment.Id,
                appointment.PatientId,
                tenantContext.TenantId,
                appointment.CancelledAt!.Value,
                timeProvider.GetUtcNow()),
            cancellationToken)
            .ConfigureAwait(false);

        metrics.AppointmentsCancelled.Add(1);
        return Result<CancelAppointmentResponse>.Ok(
            new CancelAppointmentResponse(appointment.Id, appointment.Status.ToString()));
    }
}
