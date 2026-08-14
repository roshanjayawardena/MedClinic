using Appointments.Contracts.Events;
using Core;
using Hangfire;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Domain;
using Notifications.Jobs;
using Notifications.Persistence;

namespace Notifications.Features.OnAppointmentBooked;

public sealed class OnAppointmentBookedHandler(
    IDbContextFactory<NotificationsDbContext> dbFactory,
    IBackgroundJobClient backgroundJobs,
    TimeProvider timeProvider,
    ClinicMetrics metrics,
    ILogger<OnAppointmentBookedHandler> logger)
    : INotificationHandler<AppointmentBookedIntegrationEvent>
{
    private static readonly TimeSpan ReminderLeadTime = TimeSpan.FromHours(24);

    private static readonly Action<ILogger, string, Exception?> LogScheduleFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, "ReminderScheduleFailed"),
            "Failed to schedule appointment reminder: {ExceptionType}");

    public async ValueTask Handle(
        AppointmentBookedIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        try
        {
            await HandleInternalAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Notification scheduling must never crash the appointment booking transaction.
            LogScheduleFailed(logger, ex.GetType().Name, null);
        }
    }

    private async ValueTask HandleInternalAsync(
        AppointmentBookedIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Idempotency guard — at-least-once delivery assumption.
        var alreadyHandled = await db.Notifications
            .AnyAsync(n =>
                n.AppointmentId == notification.AppointmentId &&
                n.TemplateKey == TemplateKeys.AppointmentReminder,
                cancellationToken)
            .ConfigureAwait(false);

        if (alreadyHandled)
            return;

        var now = timeProvider.GetUtcNow();
        var reminderAt = notification.ScheduledAt - ReminderLeadTime;

        // Schedule 24 h before the appointment; enqueue immediately if less than 24 h away.
        string hangfireJobId;
        if (reminderAt > now)
        {
            hangfireJobId = backgroundJobs.Schedule<AppointmentReminderJob>(
                job => job.SendAsync(
                    notification.AppointmentId,
                    notification.PatientId,
                    notification.ClinicId,
                    notification.ScheduledAt),
                reminderAt);
        }
        else
        {
            hangfireJobId = backgroundJobs.Enqueue<AppointmentReminderJob>(
                job => job.SendAsync(
                    notification.AppointmentId,
                    notification.PatientId,
                    notification.ClinicId,
                    notification.ScheduledAt));
        }

        db.Notifications.Add(Notification.Record(
            notification.PatientId,
            notification.AppointmentId,
            NotificationChannel.Sms,
            TemplateKeys.AppointmentReminder,
            NotificationStatus.Scheduled,
            hangfireJobId: hangfireJobId));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        metrics.NotificationsScheduled.Add(1);
    }
}
