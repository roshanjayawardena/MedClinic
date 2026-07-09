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

    public static Notification Record(
        Guid patientId,
        Guid? appointmentId,
        NotificationChannel channel,
        string templateKey,
        NotificationStatus status,
        DateTimeOffset? sentAt = null,
        string? failureReason = null) =>
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
        };
}
