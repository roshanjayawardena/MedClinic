using Clinics.Domain;
using Microsoft.EntityFrameworkCore;

namespace Clinics.Persistence;

/// <summary>
/// Plain DbContext — clinics are the tenants, so no tenant filter is applied here.
/// </summary>
public sealed class ClinicsDbContext(DbContextOptions<ClinicsDbContext> options) : DbContext(options)
{
    public DbSet<Clinic> Clinics => Set<Clinic>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("clinics");

        modelBuilder.Entity<Clinic>(c =>
        {
            c.ToTable("clinics");
            c.HasKey(x => x.Id);
            c.Property(x => x.Name).HasMaxLength(200).IsRequired();
            c.Property(x => x.Slug).HasMaxLength(100).IsRequired();
            c.Property(x => x.ContactEmail).HasMaxLength(300).IsRequired();
            c.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
            c.Property(x => x.Plan).HasConversion<string>().HasMaxLength(20).IsRequired();
            c.HasIndex(x => x.Slug).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}
