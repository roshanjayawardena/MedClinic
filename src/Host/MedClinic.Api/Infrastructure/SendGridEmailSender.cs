using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Notifications.Domain;
using Notifications.Infrastructure;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace MedClinic.Api.Infrastructure;

/// <summary>
/// Sends transactional email via SendGrid.
/// Configure via SendGrid:* in appsettings or environment variables.
/// Never logs recipient address — it is PHI.
/// </summary>
public sealed class SendGridEmailSender(
    IConfiguration configuration,
    ILogger<SendGridEmailSender> logger) : INotificationSender
{
    private static readonly Action<ILogger, string, Exception?> LogSent =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, "SendGridSent"),
            "Email dispatched via SendGrid: StatusCode={StatusCode}");

    private static readonly Action<ILogger, string, Exception?> LogFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(2, "SendGridFailed"),
            "SendGrid delivery failed: StatusCode={StatusCode}");

    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        if (message.Channel != NotificationChannel.Email)
            return;

        var apiKey   = configuration["SendGrid:ApiKey"]!;
        var fromAddr = configuration["SendGrid:FromEmail"] ?? "no-reply@mediclinic.local";
        var fromName = configuration["SendGrid:FromName"]  ?? "MediClinic";

        var client = new SendGridClient(apiKey);

        var msg = MailHelper.CreateSingleEmail(
            from:    new EmailAddress(fromAddr, fromName),
            to:      new EmailAddress(message.Recipient),   // PHI — never logged
            subject: "MediClinic Notification",
            plainTextContent: null,
            htmlContent: message.Body);

        var response = await client.SendEmailAsync(msg, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            LogSent(logger, ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture), null);
        else
            LogFailed(logger, ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture), null);
    }
}
