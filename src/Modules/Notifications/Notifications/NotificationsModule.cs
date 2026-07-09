using Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Infrastructure;
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
    }

    // Notifications is a pure consumer: no HTTP endpoints to register.
    public void MapEndpoints(IEndpointRouteBuilder app) { }
}
