using System.Reflection;
using Appointments.Persistence;
using Core;
using Encounters.Persistence;
using MedClinic.Migrations.PostgreSQL;
using MedClinic.Migrations.PostgreSQL.Migrations.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Identity.Domain;
using Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Patients.Persistence;
using Prescriptions.Persistence;

// Force the migrations assembly into the AppDomain so EF can discover the migration classes.
// Without a direct type reference, the assembly is copied to output but not loaded at runtime.
Assembly.Load("MedClinic.Migrations.PostgreSQL");

var host = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var connStr = host.Configuration["ConnectionStrings:DefaultConnection"]
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing from configuration.");

var tenantContext = new MigrationTenantContext();

// Register each module's DbContext pointing at the centralized migrations assembly.
// Add a new entry here whenever a new module is created (follow the add-module skill).
host.Services.AddDbContext<PatientsDbContext>(o =>
    o.UseNpgsql(connStr, npg => npg
        .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
        .MigrationsHistoryTable("__EFMigrationsHistory", "patients"))
     .ConfigureWarnings(w => w.Ignore(
         Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

host.Services.AddDbContext<AppointmentsDbContext>(o =>
    o.UseNpgsql(connStr, npg => npg
        .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
        .MigrationsHistoryTable("__EFMigrationsHistory", "appointments"))
     .ConfigureWarnings(w => w.Ignore(
         Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

host.Services.AddDbContext<EncountersDbContext>(o =>
    o.UseNpgsql(connStr, npg => npg
        .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
        .MigrationsHistoryTable("__EFMigrationsHistory", "encounters"))
     .ConfigureWarnings(w => w.Ignore(
         Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

host.Services.AddDbContext<PrescriptionsDbContext>(o =>
    o.UseNpgsql(connStr, npg => npg
        .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
        .MigrationsHistoryTable("__EFMigrationsHistory", "prescriptions"))
     .ConfigureWarnings(w => w.Ignore(
         Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

host.Services.AddDbContext<IdentityModuleDbContext>(o =>
    o.UseNpgsql(connStr, npg => npg
        .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
        .MigrationsHistoryTable("__EFMigrationsHistory", "identity"))
     .ConfigureWarnings(w => w.Ignore(
         Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

// Identity services are needed for RoleManager used during role seeding.
host.Services.AddIdentityCore<ClinicUser>()
    .AddRoles<ClinicRole>()
    .AddEntityFrameworkStores<IdentityModuleDbContext>();

host.Services.AddSingleton<ITenantContext>(tenantContext);
host.Services.AddSingleton(TimeProvider.System);

var app = host.Build();

await using var scope = app.Services.CreateAsyncScope();
var sp = scope.ServiceProvider;

Console.WriteLine("Applying MedClinic migrations...");

// Apply each module's migrations in dependency order.
// Patients first — other modules may reference patient ids.
await sp.GetRequiredService<PatientsDbContext>().Database.MigrateAsync();
Console.WriteLine("  ✓ Patients");

await sp.GetRequiredService<AppointmentsDbContext>().Database.MigrateAsync();
Console.WriteLine("  ✓ Appointments");

await sp.GetRequiredService<EncountersDbContext>().Database.MigrateAsync();
Console.WriteLine("  ✓ Encounters");

await sp.GetRequiredService<PrescriptionsDbContext>().Database.MigrateAsync();
Console.WriteLine("  ✓ Prescriptions");

await sp.GetRequiredService<IdentityModuleDbContext>().Database.MigrateAsync();
Console.WriteLine("  ✓ Identity");

// Add new modules here as they are built:

Console.WriteLine("All migrations applied successfully.");
