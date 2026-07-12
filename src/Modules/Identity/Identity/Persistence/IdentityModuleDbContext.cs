using Core;
using DomainRoles = Identity.Domain.Roles;
using Identity.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Identity.Persistence;

public sealed class IdentityModuleDbContext(
    DbContextOptions<IdentityModuleDbContext> options,
    ITenantContext tenantContext)
    : IdentityDbContext<ClinicUser, ClinicRole, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity framework tables MUST be configured before custom overrides.
        base.OnModelCreating(builder);

        builder.HasDefaultSchema("identity");

        builder.Entity<ClinicUser>(user =>
        {
            user.Property(u => u.FirstName).HasMaxLength(200).IsRequired();
            user.Property(u => u.LastName).HasMaxLength(200).IsRequired();
            // IdentityDbContext inherits a Roles DbSet property — use alias DomainRoles
            // to avoid shadowing by this.Roles (DbSet<ClinicRole>) inside this method.
            user.HasQueryFilter(u => u.ClinicId == tenantContext.TenantId);
        });

        builder.Entity<RefreshToken>(rt =>
        {
            rt.ToTable("refresh_tokens");
            rt.HasKey(r => r.Id);
            rt.Property(r => r.Token).HasMaxLength(512).IsRequired();
            rt.HasIndex(r => r.Token).IsUnique();
            rt.HasQueryFilter(r => r.ClinicId == tenantContext.TenantId);
        });

        // Seed the four canonical roles with stable IDs so every migration run
        // produces the same data. ConcurrencyStamp must be deterministic in seeded data.
        builder.Entity<ClinicRole>().HasData(
            new ClinicRole(DomainRoles.Doctor)      { Id = new Guid("11111111-0000-0000-0000-000000000001"), NormalizedName = "DOCTOR",       ConcurrencyStamp = "11111111-0000-0000-0000-000000000001" },
            new ClinicRole(DomainRoles.Pharmacist)  { Id = new Guid("11111111-0000-0000-0000-000000000002"), NormalizedName = "PHARMACIST",    ConcurrencyStamp = "11111111-0000-0000-0000-000000000002" },
            new ClinicRole(DomainRoles.Receptionist){ Id = new Guid("11111111-0000-0000-0000-000000000003"), NormalizedName = "RECEPTIONIST",  ConcurrencyStamp = "11111111-0000-0000-0000-000000000003" },
            new ClinicRole(DomainRoles.Admin)       { Id = new Guid("11111111-0000-0000-0000-000000000004"), NormalizedName = "ADMIN",         ConcurrencyStamp = "11111111-0000-0000-0000-000000000004" }
        );
    }
}
