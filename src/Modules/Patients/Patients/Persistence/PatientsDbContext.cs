using Core;
using Microsoft.EntityFrameworkCore;
using Patients.Domain;
using Persistence;

namespace Patients.Persistence;

public sealed class PatientsDbContext(
    DbContextOptions<PatientsDbContext> options,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : BaseDbContext<PatientsDbContext>(options, tenantContext, timeProvider)
{
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("patients");

        modelBuilder.Entity<Patient>(patient =>
        {
            patient.ToTable("patients");
            patient.Property(p => p.FirstName).HasMaxLength(200).IsRequired();
            patient.Property(p => p.LastName).HasMaxLength(200).IsRequired();
            patient.Property(p => p.ContactPhone).HasMaxLength(50).IsRequired();
            patient.HasIndex(p => p.TenantId);
        });

        // AuditEntry is not an AuditableEntity — it is the audit log itself (append-only).
        // The tenant filter must be applied here manually; BaseDbContext only handles
        // AuditableEntity subtypes automatically.
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

        // Always call base LAST — it applies the global tenant + soft-delete filter
        // to Patient and every other AuditableEntity subtype in this context.
        base.OnModelCreating(modelBuilder);
    }
}
