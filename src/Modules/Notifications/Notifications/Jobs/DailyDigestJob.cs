using Appointments.Contracts;
using Core;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Notifications.Domain;
using Notifications.Infrastructure;

namespace Notifications.Jobs;

/// <summary>
/// Hangfire job that sends the doctor a daily appointment schedule email.
/// Runs once per clinic — tenantId and doctorEmail come from configuration.
/// Never logs the doctor email address (treated as PII).
/// </summary>
public sealed class DailyDigestJob(
    IMediator mediator,
    INotificationSender sender,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<DailyDigestJob> logger)
{
    private static readonly Action<ILogger, int, Exception?> LogSent =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(1, "DigestSent"),
            "Daily digest sent: {AppointmentCount} appointment(s) for today.");

    private static readonly Action<ILogger, string, Exception?> LogFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(2, "DigestFailed"),
            "Daily digest failed: {ExceptionType}");

    private static readonly Action<ILogger, Exception?> LogNoEmail =
        LoggerMessage.Define(LogLevel.Warning, new EventId(3, "DigestNoEmail"),
            "Daily digest skipped: Digest:DoctorEmail is not configured.");

    private static readonly Action<ILogger, Exception?> LogNoTenant =
        LoggerMessage.Define(LogLevel.Warning, new EventId(4, "DigestNoTenant"),
            "Daily digest skipped: Digest:TenantId is not configured.");

    public async Task SendAsync()
    {
        var doctorEmail = configuration["Digest:DoctorEmail"];
        var tenantIdStr = configuration["Digest:TenantId"];

        if (string.IsNullOrWhiteSpace(doctorEmail))
        {
            LogNoEmail(logger, null);
            return;
        }

        if (!Guid.TryParse(tenantIdStr, out var tenantId))
        {
            LogNoTenant(logger, null);
            return;
        }

        // Restore tenant context — Hangfire jobs have no HttpContext.
        BackgroundJobTenantScope.Current = tenantId;

        try
        {
            var today  = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            var result = await mediator
                .Send(new GetAppointmentsByDateQuery(today))
                .ConfigureAwait(false);

            var items = result.IsSuccess ? result.Value.Items : [];

            var subject = $"MediClinic — Schedule for {today:dddd, d MMMM yyyy} ({items.Count} appointment{(items.Count == 1 ? "" : "s")})";
            var body    = BuildHtml(today, items);

            // doctorEmail is PII — passed directly to sender, never logged.
            await sender
                .SendAsync(new NotificationMessage(NotificationChannel.Email, doctorEmail, body, subject), CancellationToken.None)
                .ConfigureAwait(false);

            LogSent(logger, items.Count, null);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex.GetType().Name, null);
            throw; // let Hangfire retry
        }
    }

    private static string BuildHtml(DateOnly date, IReadOnlyList<AppointmentSummaryDto> items)
    {
        var rows = items.Count == 0
            ? "<tr><td colspan=\"4\" style=\"padding:24px;text-align:center;color:#94a3b8;font-size:14px;\">No appointments scheduled for today.</td></tr>"
            : string.Join("\n", items.Select((a, i) => $"""
                <tr style="background:{(i % 2 == 0 ? "#ffffff" : "#f8fafc")}">
                  <td style="padding:12px 16px;font-weight:600;color:#0f172a;white-space:nowrap;">
                    {a.ScheduledAt.ToLocalTime():h:mm tt}
                  </td>
                  <td style="padding:12px 16px;color:#1e293b;">
                    {a.PatientFirstName} {a.PatientLastName}
                  </td>
                  <td style="padding:12px 16px;color:#475569;">
                    {System.Net.WebUtility.HtmlEncode(a.Reason)}
                  </td>
                  <td style="padding:12px 16px;">
                    <span style="
                      display:inline-block;padding:2px 10px;border-radius:999px;font-size:12px;font-weight:600;
                      background:{StatusBg(a.Status)};color:{StatusFg(a.Status)};">
                      {a.Status}
                    </span>
                  </td>
                </tr>
                """));

        return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"/></head>
            <body style="margin:0;padding:0;background:#f1f5f9;font-family:'Segoe UI',Arial,sans-serif;">
              <table width="100%" cellpadding="0" cellspacing="0" style="background:#f1f5f9;padding:32px 0;">
                <tr><td align="center">
                  <table width="620" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.08);">

                    <!-- Header -->
                    <tr>
                      <td style="background:linear-gradient(135deg,#1d4ed8,#3b82f6);padding:28px 32px;">
                        <div style="font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.1em;color:#bfdbfe;margin-bottom:6px;">
                          Daily Schedule
                        </div>
                        <div style="font-size:22px;font-weight:700;color:#ffffff;">
                          {date:dddd, d MMMM yyyy}
                        </div>
                        <div style="font-size:14px;color:#bfdbfe;margin-top:4px;">
                          {items.Count} appointment{(items.Count == 1 ? "" : "s")} scheduled
                        </div>
                      </td>
                    </tr>

                    <!-- Table -->
                    <tr>
                      <td style="padding:0;">
                        <table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;">
                          <thead>
                            <tr style="background:#f8fafc;border-bottom:2px solid #e2e8f0;">
                              <th style="padding:10px 16px;text-align:left;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.06em;color:#64748b;">Time</th>
                              <th style="padding:10px 16px;text-align:left;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.06em;color:#64748b;">Patient</th>
                              <th style="padding:10px 16px;text-align:left;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.06em;color:#64748b;">Reason</th>
                              <th style="padding:10px 16px;text-align:left;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.06em;color:#64748b;">Status</th>
                            </tr>
                          </thead>
                          <tbody>
                            {rows}
                          </tbody>
                        </table>
                      </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                      <td style="padding:20px 32px;border-top:1px solid #f1f5f9;background:#fafafa;">
                        <p style="margin:0;font-size:12px;color:#94a3b8;text-align:center;">
                          MediClinic · This digest was generated at {DateTimeOffset.UtcNow:HH:mm} UTC
                          · <a href="#" style="color:#3b82f6;text-decoration:none;">View full schedule</a>
                        </p>
                      </td>
                    </tr>

                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string StatusBg(string status) => status switch
    {
        "Scheduled"  => "#dbeafe",
        "CheckedIn"  => "#fef9c3",
        "Completed"  => "#dcfce7",
        "Cancelled"  => "#fee2e2",
        _            => "#f1f5f9",
    };

    private static string StatusFg(string status) => status switch
    {
        "Scheduled"  => "#1d4ed8",
        "CheckedIn"  => "#a16207",
        "Completed"  => "#166534",
        "Cancelled"  => "#b91c1c",
        _            => "#475569",
    };
}
