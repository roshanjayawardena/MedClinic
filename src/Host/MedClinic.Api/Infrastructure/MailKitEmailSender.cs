using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Notifications.Domain;
using Notifications.Infrastructure;

namespace MedClinic.Api.Infrastructure;

/// <summary>
/// Sends emails via SMTP using MailKit.
/// Configure via Email:* in appsettings or environment variables.
/// Never logs recipient address — it is PHI.
/// </summary>
public sealed class MailKitEmailSender(
    IConfiguration configuration,
    ILogger<MailKitEmailSender> logger) : INotificationSender
{
    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        if (message.Channel != NotificationChannel.Email)
            return;

        var from     = configuration["Email:FromAddress"]!;
        var fromName = configuration["Email:FromName"] ?? "MediClinic";

        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(fromName, from));
        mimeMessage.To.Add(MailboxAddress.Parse(message.Recipient));
        mimeMessage.Subject = "MediClinic Notification";
        mimeMessage.Body    = new TextPart("html") { Text = message.Body };

        using var smtp  = new SmtpClient();
        var host   = configuration["Email:SmtpHost"]!;
        var port   = configuration.GetValue<int>("Email:SmtpPort", 587);
        var useSsl = configuration.GetValue<bool>("Email:UseSsl", false);

        await smtp.ConnectAsync(
            host, port,
            useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
            cancellationToken).ConfigureAwait(false);

        var user = configuration["Email:SmtpUser"];
        var pass = configuration["Email:SmtpPassword"];
        if (!string.IsNullOrEmpty(user))
            await smtp.AuthenticateAsync(user, pass, cancellationToken).ConfigureAwait(false);

        await smtp.SendAsync(mimeMessage, cancellationToken).ConfigureAwait(false);
        await smtp.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Email sent via MailKit: Channel={Channel}", message.Channel);
    }
}
