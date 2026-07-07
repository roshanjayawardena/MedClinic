using Appointments.Domain;
using Core;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Appointments.Persistence;

public sealed class AppointmentsDbContext(
    DbContextOptions<AppointmentsDbContext> options,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : BaseDbContext<AppointmentsDbContext>(options, tenantContext, timeProvider)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("appointments");

        modelBuilder.Entity<Appointment>(a =>
        {
            a.ToTable("appointments");
            a.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            a.Property(x => x.CancellationReason).HasMaxLength(500);
            a.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            a.HasIndex(x => x.TenantId);
            a.HasIndex(x => new { x.TenantId, x.ScheduledAt });
        });

        // Always call base LAST — applies global tenant + soft-delete filter.
        base.OnModelCreating(modelBuilder);
    }
}
