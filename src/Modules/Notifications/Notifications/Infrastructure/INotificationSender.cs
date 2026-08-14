using Notifications.Domain;

namespace Notifications.Infrastructure;

public sealed record NotificationMessage(
    NotificationChannel Channel,
    string Recipient,  // PHI — never log; use immediately and discard
    string Body,
    string Subject = "MediClinic Notification");

public interface INotificationSender
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}
