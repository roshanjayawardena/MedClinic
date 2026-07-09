using System.Diagnostics.Metrics;

namespace Core;

/// <summary>
/// Application-level metrics for the clinic domain.
/// Registered as a singleton; injected into handlers that need to record business events.
/// OpenTelemetry picks up the "MediClinic" meter name in Program.cs.
/// </summary>
public sealed class ClinicMetrics : IDisposable
{
    public const string MeterName = "MediClinic";

    private readonly Meter _meter;

    public Counter<long> AppointmentsBooked { get; }
    public Counter<long> AppointmentsCancelled { get; }
    public Counter<long> NotificationsScheduled { get; }
    public Counter<long> NotificationsSent { get; }
    public Counter<long> NotificationsFailed { get; }
    public Counter<long> NotificationsConsentDenied { get; }
    public Counter<long> LoginSuccess { get; }
    public Counter<long> LoginFailed { get; }

    public ClinicMetrics()
    {
        _meter = new Meter(MeterName, "1.0");

        AppointmentsBooked = _meter.CreateCounter<long>(
            "mediclinic.appointments.booked",
            description: "Total appointments booked");
        AppointmentsCancelled = _meter.CreateCounter<long>(
            "mediclinic.appointments.cancelled",
            description: "Total appointments cancelled");
        NotificationsScheduled = _meter.CreateCounter<long>(
            "mediclinic.notifications.scheduled",
            description: "Reminder jobs scheduled in Hangfire");
        NotificationsSent = _meter.CreateCounter<long>(
            "mediclinic.notifications.sent",
            description: "Reminder SMS messages successfully sent");
        NotificationsFailed = _meter.CreateCounter<long>(
            "mediclinic.notifications.failed",
            description: "Reminder send attempts that failed");
        NotificationsConsentDenied = _meter.CreateCounter<long>(
            "mediclinic.notifications.consent_denied",
            description: "Reminders suppressed due to missing consent");
        LoginSuccess = _meter.CreateCounter<long>(
            "mediclinic.auth.login.success",
            description: "Successful login attempts");
        LoginFailed = _meter.CreateCounter<long>(
            "mediclinic.auth.login.failed",
            description: "Failed login attempts — monitor for brute-force patterns");
    }

    public void Dispose() => _meter.Dispose();
}
