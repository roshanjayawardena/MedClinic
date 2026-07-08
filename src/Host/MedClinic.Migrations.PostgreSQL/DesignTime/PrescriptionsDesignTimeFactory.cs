using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Prescriptions.Persistence;

namespace MedClinic.Migrations.PostgreSQL.DesignTime;

internal sealed class PrescriptionsDesignTimeFactory : IDesignTimeDbContextFactory<PrescriptionsDbContext>
{
    public PrescriptionsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PrescriptionsDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5433;Database=mediclinic_dev;Username=postgres;Password=postgres",
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "prescriptions"))
            .Options;

        return new PrescriptionsDbContext(options, new MigrationTenantContext(), TimeProvider.System);
    }
}
