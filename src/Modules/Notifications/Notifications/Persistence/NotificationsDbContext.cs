using Core;
using Microsoft.EntityFrameworkCore;
using Notifications.Domain;
using Persistence;

namespace Notifications.Persistence;

public sealed class NotificationsDbContext(
    DbContextOptions<NotificationsDbContext> options,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : BaseDbContext<NotificationsDbContext>(options, tenantContext, timeProvider)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");

        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("notifications");

            e.Property(x => x.Channel)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            e.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            e.Property(x => x.TemplateKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.FailureReason).HasMaxLength(200);

            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            // Idempotency index: one notification per patient+appointment+template
            e.HasIndex(x => new { x.TenantId, x.AppointmentId, x.TemplateKey })
                .HasFilter($"\"{nameof(Notification.AppointmentId)}\" IS NOT NULL");
        });

        // Always call base LAST — applies global tenant + soft-delete filter to Notification.
        base.OnModelCreating(modelBuilder);
    }
}
