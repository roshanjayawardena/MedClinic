using Core;
using Encounters.Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Encounters.Persistence;

public sealed class EncountersDbContext(
    DbContextOptions<EncountersDbContext> options,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : BaseDbContext<EncountersDbContext>(options, tenantContext, timeProvider)
{
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("encounters");

        modelBuilder.Entity<Encounter>(e =>
        {
            e.ToTable("encounters");
            e.Property(x => x.ClinicalNotes).HasMaxLength(4000);
            e.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => new { x.TenantId, x.PatientId });

            // VitalSigns: owned entity — columns inline in the encounters table.
            e.OwnsOne(x => x.Vitals, v =>
            {
                v.Property(p => p.SystolicBp).HasColumnName("vitals_systolic_bp");
                v.Property(p => p.DiastolicBp).HasColumnName("vitals_diastolic_bp");
                v.Property(p => p.HeartRateBpm).HasColumnName("vitals_heart_rate_bpm");
                v.Property(p => p.TemperatureCelsius).HasColumnName("vitals_temperature_celsius").HasPrecision(4, 1);
                v.Property(p => p.RespiratoryRatePerMin).HasColumnName("vitals_respiratory_rate");
                v.Property(p => p.OxygenSaturationPercent).HasColumnName("vitals_spo2_percent");
                v.Property(p => p.WeightKg).HasColumnName("vitals_weight_kg").HasPrecision(5, 1);
            });

            // Diagnoses: owned entity collection — separate table, shadow PK.
            e.OwnsMany(x => x.Diagnoses, d =>
            {
                d.ToTable("encounter_diagnoses");
                d.WithOwner().HasForeignKey("EncounterId");
                d.Property<int>("Id").ValueGeneratedOnAdd();
                d.HasKey("Id");
                d.Property(p => p.Icd10Code).HasMaxLength(20).IsRequired();
                d.Property(p => p.Description).HasMaxLength(500).IsRequired();
                d.Property(p => p.Type)
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .IsRequired();
            });

            // Tell EF Core to use the private backing field for Diagnoses.
            e.Navigation(x => x.Diagnoses)
                .HasField("_diagnoses")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        // AuditEntry is not an AuditableEntity — manual tenant filter required.
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

        // Always call base LAST — applies global tenant + soft-delete filter to Encounter.
        base.OnModelCreating(modelBuilder);
    }
}
