using Core;
using Microsoft.EntityFrameworkCore;

namespace Patients;

public sealed class PatientsDbContext(DbContextOptions<PatientsDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Patient>(patient =>
        {
            patient.ToTable("patients");
            patient.HasKey(p => p.Id);
            patient.Property(p => p.FirstName).HasMaxLength(200).IsRequired();
            patient.Property(p => p.LastName).HasMaxLength(200).IsRequired();
            patient.Property(p => p.ContactPhone).HasMaxLength(50).IsRequired();
            patient.HasIndex(p => p.TenantId);

            // Tenant isolation: every query against Patients is filtered to the
            // current tenant. Never remove this filter or query Patients via a
            // path that bypasses it (see .agents/rules/phi-and-tenancy.md).
            patient.HasQueryFilter(p => p.TenantId == tenantContext.TenantId);
        });

        modelBuilder.Entity<AuditEntry>(audit =>
        {
            audit.ToTable("patients_audit_entries");
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Action).HasMaxLength(100).IsRequired();
            audit.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            audit.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
            audit.Property(a => a.PerformedBy).HasMaxLength(200);
            audit.HasIndex(a => a.TenantId);

            audit.HasQueryFilter(a => a.TenantId == tenantContext.TenantId);
        });
    }
}
