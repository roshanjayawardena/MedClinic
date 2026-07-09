using Appointments.Contracts.Events;
using Hangfire;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Notifications.Domain;
using Notifications.Persistence;

namespace Notifications.Features.OnAppointmentCancelled;

public sealed class OnAppointmentCancelledHandler(
    IDbContextFactory<NotificationsDbContext> dbFactory,
    IBackgroundJobClient backgroundJobs)
    : INotificationHandler<AppointmentCancelledIntegrationEvent>
{
    public async ValueTask Handle(
        AppointmentCancelledIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await db.Notifications
            .SingleOrDefaultAsync(n =>
                n.AppointmentId == notification.AppointmentId &&
                n.TemplateKey == TemplateKeys.AppointmentReminder &&
                n.Status == NotificationStatus.Scheduled,
                cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
            return; // Already sent, or no reminder was ever scheduled (e.g. consent denied).

        if (record.HangfireJobId is not null)
            backgroundJobs.Delete(record.HangfireJobId);

        record.MarkCancelled();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
