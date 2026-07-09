using Billing.Contracts.Events;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Domain;
using Notifications.Infrastructure;
using Notifications.Persistence;
using Patients.Contracts;

namespace Notifications.Features.OnInvoicePaid;

public sealed class OnInvoicePaidHandler(
    IDbContextFactory<NotificationsDbContext> dbFactory,
    IMediator mediator,
    INotificationSender sender,
    TimeProvider timeProvider,
    ILogger<OnInvoicePaidHandler> logger)
    : INotificationHandler<InvoicePaidIntegrationEvent>
{
    public async ValueTask Handle(
        InvoicePaidIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Idempotency: one payment confirmation per invoice.
        var alreadyHandled = await db.Notifications
            .AnyAsync(n => n.AppointmentId == null
                        && n.PatientId == notification.PatientId
                        && n.TemplateKey == TemplateKeys.PaymentConfirmation,
                       cancellationToken)
            .ConfigureAwait(false);

        if (alreadyHandled)
            return;

        var contactResult = await mediator
            .Send(new GetPatientContactQuery(notification.PatientId), cancellationToken)
            .ConfigureAwait(false);

        if (contactResult.IsFailure)
        {
            logger.LogWarning("GetPatientContact failed: {Code}", contactResult.Error!.Code);
            db.Notifications.Add(Notification.Record(
                notification.PatientId,
                appointmentId: null,
                NotificationChannel.Sms,
                TemplateKeys.PaymentConfirmation,
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
                appointmentId: null,
                NotificationChannel.Sms,
                TemplateKeys.PaymentConfirmation,
                NotificationStatus.ConsentDenied));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var body = $"Payment of ${notification.AmountPaid:F2} received via {notification.PaymentMethod}. Thank you for visiting.";

        var now = timeProvider.GetUtcNow();
        NotificationStatus status;
        DateTimeOffset? sentAt = null;
        string? failureReason = null;

        try
        {
            // Recipient is PHI — passed directly to sender, never logged.
            await sender
                .SendAsync(new NotificationMessage(NotificationChannel.Sms, contact.ContactPhone, body), cancellationToken)
                .ConfigureAwait(false);
            status = NotificationStatus.Sent;
            sentAt = now;
        }
        catch (Exception ex)
        {
            logger.LogError("SMS send failed: {ExceptionType}", ex.GetType().Name);
            status = NotificationStatus.Failed;
            failureReason = ex.GetType().Name;
        }

        db.Notifications.Add(Notification.Record(
            notification.PatientId,
            appointmentId: null,
            NotificationChannel.Sms,
            TemplateKeys.PaymentConfirmation,
            status,
            sentAt,
            failureReason));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
