using Encounters.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedClinic.Migrations.PostgreSQL.DesignTime;

internal sealed class EncountersDesignTimeFactory : IDesignTimeDbContextFactory<EncountersDbContext>
{
    public EncountersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EncountersDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5433;Database=mediclinic_dev;Username=postgres;Password=postgres",
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "encounters"))
            .Options;

        return new EncountersDbContext(options, new MigrationTenantContext(), TimeProvider.System);
    }
}
