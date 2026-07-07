using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Patients.Persistence;

namespace MedClinic.Migrations.PostgreSQL.DesignTime;

/// <summary>
/// Used by "dotnet ef migrations add" at design time.
/// Connection string here is for local dev only — the DbMigrator handles production.
/// </summary>
internal sealed class PatientsDesignTimeFactory : IDesignTimeDbContextFactory<PatientsDbContext>
{
    public PatientsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PatientsDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5433;Database=mediclinic_dev;Username=postgres;Password=postgres",
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "patients"))
            .Options;

        return new PatientsDbContext(options, new MigrationTenantContext(), TimeProvider.System);
    }
}
