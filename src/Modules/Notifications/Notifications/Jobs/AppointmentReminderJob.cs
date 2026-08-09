using Core;
using Mediator;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Domain;
using Notifications.Infrastructure;
using Notifications.Persistence;
using Patients.Contracts;

namespace Notifications.Jobs;

/// <summary>
/// Hangfire background job — executes 24 hours before the appointment.
/// Sets BackgroundJobTenantScope so HttpTenantContext resolves correctly
/// without an active HTTP request.
/// </summary>
public sealed class AppointmentReminderJob(
    IDbContextFactory<NotificationsDbContext> dbFactory,
    IMediator mediator,
    INotificationSender sender,
    TimeProvider timeProvider,
    ClinicMetrics metrics,
    ILogger<AppointmentReminderJob> logger)
{
    private static readonly Action<ILogger, string, Exception?> LogContactFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "ReminderContactFailed"),
            "Reminder job: GetPatientContact failed: {Code}");

    private static readonly Action<ILogger, string, Exception?> LogSmsFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(2, "ReminderSmsFailed"),
            "Appointment reminder SMS failed: {ExceptionType}");

    public async Task SendAsync(
        Guid appointmentId,
        Guid patientId,
        Guid clinicId,
        DateTimeOffset scheduledAt)
    {
        // Restore tenant context — HttpTenantContext reads this when HttpContext is absent.
        BackgroundJobTenantScope.Current = clinicId;

        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);

        var record = await db.Notifications
            .SingleOrDefaultAsync(n =>
                n.AppointmentId == appointmentId &&
                n.TemplateKey == TemplateKeys.AppointmentReminder)
            .ConfigureAwait(false);

        // Idempotency: already sent or appointment was cancelled after this job was scheduled.
        if (record is null ||
            record.Status is NotificationStatus.Sent or NotificationStatus.Cancelled)
            return;

        var contactResult = await mediator
            .Send(new GetPatientContactQuery(patientId))
            .ConfigureAwait(false);

        if (contactResult.IsFailure)
        {
            LogContactFailed(logger, contactResult.Error!.Code, null);
            record.MarkFailed(contactResult.Error.Code);
            await db.SaveChangesAsync().ConfigureAwait(false);
            return;
        }

        var contact = contactResult.Value;

        if (!contact.ConsentToCommunications)
        {
            record.MarkConsentDenied();
            await db.SaveChangesAsync().ConfigureAwait(false);
            metrics.NotificationsConsentDenied.Add(1);
            return;
        }

        var body = $"Reminder: your appointment is on {scheduledAt.LocalDateTime:dddd d MMMM yyyy 'at' h:mm tt}. Please arrive 10 minutes early.";

        try
        {
            // contact.ContactPhone is PHI — passed to sender, never logged.
            // Hangfire jobs have no CancellationToken in serialized expressions; use None.
            await sender
                .SendAsync(new NotificationMessage(NotificationChannel.Sms, contact.ContactPhone, body), CancellationToken.None)
                .ConfigureAwait(false);
            record.MarkSent(timeProvider.GetUtcNow());
            metrics.NotificationsSent.Add(1);
        }
        catch (Exception ex)
        {
            // Log only exception type — the message may contain the phone number.
            LogSmsFailed(logger, ex.GetType().Name, null);
            record.MarkFailed(ex.GetType().Name);
            metrics.NotificationsFailed.Add(1);
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }
}
