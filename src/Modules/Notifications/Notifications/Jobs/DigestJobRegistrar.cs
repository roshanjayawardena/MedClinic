using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Notifications.Jobs;

/// <summary>
/// Registers the DailyDigestJob as a Hangfire recurring job at startup.
/// Cron defaults to 07:00 UTC daily. Override via Digest:CronExpression.
/// </summary>
public sealed class DigestJobRegistrar(
    IRecurringJobManager recurringJobs,
    IConfiguration configuration,
    ILogger<DigestJobRegistrar> logger) : IHostedService
{
    private const string JobId = "daily-appointment-digest";

    private static readonly Action<ILogger, string, Exception?> LogRegistered =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, "DigestRegistered"),
            "Daily digest job registered with cron '{Cron}'.");

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cron = configuration["Digest:CronExpression"] ?? Cron.Daily(7);

        recurringJobs.AddOrUpdate<DailyDigestJob>(
            JobId,
            job => job.SendAsync(),
            cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        LogRegistered(logger, cron, null);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
