using Core;
using MedClinic.Migrations.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Patients;

var host = Host.CreateApplicationBuilder(args);

var connStr = host.Configuration["ConnectionStrings:DefaultConnection"]
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing from configuration.");

var tenantContext = new MigrationTenantContext();

// Register each module's DbContext with the central migrations assembly.
// Add a new entry here whenever a new module is created (follow the add-module skill).
host.Services.AddDbContext<PatientsDbContext>(o =>
    o.UseNpgsql(connStr, npg => npg
        .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
        .MigrationsHistoryTable("__EFMigrationsHistory", "patients")));

// Provide the stub tenant context for migration runtime
host.Services.AddSingleton<ITenantContext>(tenantContext);

var app = host.Build();

await using var scope = app.Services.CreateAsyncScope();
var sp = scope.ServiceProvider;

Console.WriteLine("Applying MedClinic migrations...");

// Apply each module's migrations in dependency order.
// Patients first — other modules may reference patient ids.
await sp.GetRequiredService<PatientsDbContext>().Database.MigrateAsync();
Console.WriteLine("  ✓ Patients");

// Add new modules here as they are built:
// await sp.GetRequiredService<AppointmentsDbContext>().Database.MigrateAsync();
// Console.WriteLine("  ✓ Appointments");

Console.WriteLine("All migrations applied successfully.");
