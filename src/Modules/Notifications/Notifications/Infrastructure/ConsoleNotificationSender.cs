using Microsoft.Extensions.Logging;

namespace Notifications.Infrastructure;

/// <summary>
/// Development stub — writes to console without logging the recipient (PHI).
/// Replace with TwilioNotificationSender or SendGridNotificationSender in production
/// by registering a different INotificationSender implementation in NotificationsModule.
/// </summary>
public sealed class ConsoleNotificationSender(ILogger<ConsoleNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        // IMPORTANT: Never log message.Recipient — it is PHI (patient phone number or email).
        logger.LogInformation(
            "[NOTIFICATION STUB] Channel={Channel} Template body length={Length} chars. (recipient suppressed — PHI)",
            message.Channel,
            message.Body.Length);

        return Task.CompletedTask;
    }
}
