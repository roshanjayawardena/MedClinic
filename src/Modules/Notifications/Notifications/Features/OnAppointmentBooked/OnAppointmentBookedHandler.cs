using Appointments.Contracts.Events;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Domain;
using Notifications.Infrastructure;
using Notifications.Persistence;
using Patients.Contracts;

namespace Notifications.Features.OnAppointmentBooked;

public sealed class OnAppointmentBookedHandler(
    IDbContextFactory<NotificationsDbContext> dbFactory,
    IMediator mediator,
    INotificationSender sender,
    TimeProvider timeProvider,
    ILogger<OnAppointmentBookedHandler> logger)
    : INotificationHandler<AppointmentBookedIntegrationEvent>
{
    public async ValueTask Handle(
        AppointmentBookedIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Idempotency guard — at-least-once delivery assumption.
        var alreadyHandled = await db.Notifications
            .AnyAsync(n => n.AppointmentId == notification.AppointmentId
                        && n.TemplateKey == TemplateKeys.AppointmentReminder,
                       cancellationToken)
            .ConfigureAwait(false);

        if (alreadyHandled)
            return;

        // Cross-module query via Patients.Contracts only — minimal data, consent + phone only.
        var contactResult = await mediator
            .Send(new GetPatientContactQuery(notification.PatientId), cancellationToken)
            .ConfigureAwait(false);

        if (contactResult.IsFailure)
        {
            // Never log patientId without careful consideration — log only the error code.
            logger.LogWarning("GetPatientContact failed: {Code}", contactResult.Error!.Code);
            db.Notifications.Add(Notification.Record(
                notification.PatientId,
                notification.AppointmentId,
                NotificationChannel.Sms,
                TemplateKeys.AppointmentReminder,
                NotificationStatus.Failed,
                failureReason: contactResult.Error.Code));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var contact = contactResult.Value;

        if (!contact.ConsentToCommunications)
        {
            db.Notifications.Add(Notification.Record(
                notification.PatientId,
                notification.AppointmentId,
                NotificationChannel.Sms,
                TemplateKeys.AppointmentReminder,
                NotificationStatus.ConsentDenied));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var body = $"Reminder: your appointment is on {notification.ScheduledAt:dddd d MMMM yyyy 'at' h:mm tt}. Please arrive 10 minutes early.";

        var now = timeProvider.GetUtcNow();
        NotificationStatus status;
        DateTimeOffset? sentAt = null;
        string? failureReason = null;

        try
        {
            // Recipient (contact.ContactPhone) is PHI — passed directly to sender, never logged.
            await sender
                .SendAsync(new NotificationMessage(NotificationChannel.Sms, contact.ContactPhone, body), cancellationToken)
                .ConfigureAwait(false);
            status = NotificationStatus.Sent;
            sentAt = now;
        }
        catch (Exception ex)
        {
            // Log only the exception type — the message may contain the phone number.
            logger.LogError("SMS send failed: {ExceptionType}", ex.GetType().Name);
            status = NotificationStatus.Failed;
            failureReason = ex.GetType().Name;
        }

        db.Notifications.Add(Notification.Record(
            notification.PatientId,
            notification.AppointmentId,
            NotificationChannel.Sms,
            TemplateKeys.AppointmentReminder,
            status,
            sentAt,
            failureReason));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
