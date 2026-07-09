namespace Notifications.Domain;

public enum NotificationStatus
{
    Scheduled,      // Hangfire job queued — reminder not yet sent
    Sent,
    Failed,
    ConsentDenied,
    Cancelled,      // Appointment was cancelled; job deleted before firing
}
