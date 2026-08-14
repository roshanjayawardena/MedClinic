using Core;
using Hangfire;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Infrastructure;
using Notifications.Jobs;
using Notifications.Persistence;

[assembly: MedClinicModule(typeof(Notifications.NotificationsModule), order: 70)]

namespace Notifications;

public sealed class NotificationsModule : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<NotificationsDbContext>((sp, options) =>
            options.UseNpgsql(
                configuration["ConnectionStrings:DefaultConnection"],
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "notifications")));

        // Swap ConsoleNotificationSender for a real provider (Twilio, SendGrid, etc.)
        // by changing this single registration — the handlers are not affected.
        services.AddScoped<INotificationSender, ConsoleNotificationSender>();

        // Hangfire jobs are resolved from DI.
        // Storage is configured in Program.cs where Hangfire.PostgreSql is available.
        services.AddScoped<AppointmentReminderJob>();
        services.AddScoped<DailyDigestJob>();

        // Registers the daily digest as a recurring Hangfire job on startup.
        services.AddSingleton<IHostedService, DigestJobRegistrar>();
    }

    // Notifications is a pure consumer: no HTTP endpoints to register.
    public void MapEndpoints(IEndpointRouteBuilder app) { }
}
