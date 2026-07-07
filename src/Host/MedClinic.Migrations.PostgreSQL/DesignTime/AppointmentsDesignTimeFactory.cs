using Appointments.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedClinic.Migrations.PostgreSQL.DesignTime;

internal sealed class AppointmentsDesignTimeFactory : IDesignTimeDbContextFactory<AppointmentsDbContext>
{
    public AppointmentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppointmentsDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5433;Database=mediclinic_dev;Username=postgres;Password=postgres",
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "appointments"))
            .Options;

        return new AppointmentsDbContext(options, new MigrationTenantContext(), TimeProvider.System);
    }
}
