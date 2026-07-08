using Core;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Prescriptions.Domain;

namespace Prescriptions.Persistence;

public sealed class PrescriptionsDbContext(
    DbContextOptions<PrescriptionsDbContext> options,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : BaseDbContext<PrescriptionsDbContext>(options, tenantContext, timeProvider)
{
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<ClosedEncounterRecord> ClosedEncounters => Set<ClosedEncounterRecord>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("prescriptions");

        modelBuilder.Entity<Prescription>(p =>
        {
            p.ToTable("prescriptions");
            p.Property(x => x.DrugName).HasMaxLength(300).IsRequired();
            p.Property(x => x.DosageInstructions).HasMaxLength(500).IsRequired();
            p.Property(x => x.Status).HasConversion<string>().HasMaxLength(50);
            p.Property(x => x.CancellationReason).HasMaxLength(500);
            p.HasIndex(x => x.TenantId);
            p.HasIndex(x => x.EncounterId);
            p.HasIndex(x => x.PatientId);
        });

        // ClosedEncounterRecord is a read model (projection), not an AuditableEntity.
        // Tenant filter and key are configured manually.
        modelBuilder.Entity<ClosedEncounterRecord>(r =>
        {
            r.ToTable("closed_encounter_records");
            r.HasKey(x => x.EncounterId);
            r.HasIndex(x => x.TenantId);
            r.HasQueryFilter(x => x.TenantId == tenantContext.TenantId);
        });

        // AuditEntry is append-only and not an AuditableEntity — filter manually.
        modelBuilder.Entity<AuditEntry>(audit =>
        {
            audit.ToTable("audit_entries");
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Action).HasMaxLength(100).IsRequired();
            audit.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            audit.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
            audit.Property(a => a.PerformedBy).HasMaxLength(200);
            audit.HasIndex(a => a.TenantId);
            audit.HasQueryFilter(a => a.TenantId == tenantContext.TenantId);
        });

        base.OnModelCreating(modelBuilder);
    }
}
