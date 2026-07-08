using Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedClinic.Migrations.PostgreSQL.DesignTime;

internal sealed class IdentityDesignTimeFactory : IDesignTimeDbContextFactory<IdentityModuleDbContext>
{
    public IdentityModuleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityModuleDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5433;Database=mediclinic_dev;Username=postgres;Password=postgres",
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "identity"))
            .Options;

        return new IdentityModuleDbContext(options, new MigrationTenantContext());
    }
}
