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
    ILogger<AppointmentReminderJob> logger)
{
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
            logger.LogWarning("Reminder job: GetPatientContact failed: {Code}", contactResult.Error!.Code);
            record.MarkFailed(contactResult.Error.Code);
            await db.SaveChangesAsync().ConfigureAwait(false);
            return;
        }

        var contact = contactResult.Value;

        if (!contact.ConsentToCommunications)
        {
            record.MarkConsentDenied();
            await db.SaveChangesAsync().ConfigureAwait(false);
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
        }
        catch (Exception ex)
        {
            // Log only exception type — the message may contain the phone number.
            logger.LogError("Appointment reminder SMS failed: {ExceptionType}", ex.GetType().Name);
            record.MarkFailed(ex.GetType().Name);
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }
}
