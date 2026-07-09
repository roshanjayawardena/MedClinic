using Core;

namespace Notifications.Domain;

public sealed class Notification : AuditableEntity
{
    private Notification() { }

    public Guid PatientId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationStatus Status { get; private set; }
    public string TemplateKey { get; private set; } = string.Empty;
    public DateTimeOffset? SentAt { get; private set; }

    // Never store the failure reason verbatim from the provider — it may contain PHI (e.g. "Invalid number: +64...").
    public string? FailureReason { get; private set; }

    // Hangfire job ID — set when a reminder is scheduled; cleared on send or cancellation.
    public string? HangfireJobId { get; private set; }

    public static Notification Record(
        Guid patientId,
        Guid? appointmentId,
        NotificationChannel channel,
        string templateKey,
        NotificationStatus status,
        DateTimeOffset? sentAt = null,
        string? failureReason = null,
        string? hangfireJobId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            AppointmentId = appointmentId,
            Channel = channel,
            TemplateKey = templateKey,
            Status = status,
            SentAt = sentAt,
            FailureReason = failureReason,
            HangfireJobId = hangfireJobId,
        };

    public void MarkSent(DateTimeOffset sentAt)
    {
        Status = NotificationStatus.Sent;
        SentAt = sentAt;
        HangfireJobId = null;
    }

    public void MarkFailed(string reason)
    {
        Status = NotificationStatus.Failed;
        FailureReason = reason;
    }

    public void MarkConsentDenied()
    {
        Status = NotificationStatus.ConsentDenied;
    }

    public void MarkCancelled()
    {
        Status = NotificationStatus.Cancelled;
        HangfireJobId = null;
    }
}
