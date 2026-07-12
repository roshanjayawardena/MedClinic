using Identity.Domain;
using Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Demo clinic — stable ID so re-runs are idempotent.
var DemoClinicId = new Guid("aaaaaaaa-0000-0000-0000-000000000001");

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        var conn = ctx.Configuration.GetConnectionString("DefaultConnection")!;

        services.AddDbContext<IdentityModuleDbContext>((sp, opts) =>
            opts.UseNpgsql(conn, npg =>
                npg.MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                   .MigrationsHistoryTable("__EFMigrationsHistory", "identity")));

        // Provide a static tenant context so Identity's query filter resolves.
        services.AddSingleton<Core.ITenantContext>(new StaticTenantContext(DemoClinicId));
        services.AddSingleton(TimeProvider.System);

        services.AddIdentityCore<ClinicUser>(opts =>
        {
            opts.Password.RequireDigit         = true;
            opts.Password.RequiredLength        = 8;
            opts.Password.RequireNonAlphanumeric = false;
            opts.Password.RequireUppercase      = false;
        })
        .AddRoles<ClinicRole>()
        .AddEntityFrameworkStores<IdentityModuleDbContext>()
        .AddDefaultTokenProviders();
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("DemoSeeder starting for ClinicId={ClinicId}", DemoClinicId);

using var scope = host.Services.CreateScope();
var sp = scope.ServiceProvider;
var db = sp.GetRequiredService<IdentityModuleDbContext>();
var userMgr = sp.GetRequiredService<UserManager<ClinicUser>>();
var roleMgr  = sp.GetRequiredService<RoleManager<ClinicRole>>();

// ── Seed roles (idempotent — Identity already seeds via HasData, this is a safety net) ──
foreach (var roleName in new[] { Roles.Doctor, Roles.Pharmacist, Roles.Receptionist, Roles.Admin })
{
    if (!await roleMgr.RoleExistsAsync(roleName))
    {
        await roleMgr.CreateAsync(new ClinicRole(roleName));
        logger.LogInformation("Created role {Role}", roleName);
    }
}

// ── Seed demo users ───────────────────────────────────────────────────────────
await SeedUserAsync("admin@demo.clinic",     "Admin",    "User",       Roles.Admin,        "Admin1234!");
await SeedUserAsync("doctor@demo.clinic",    "Dr. Sarah","Perera",     Roles.Doctor,       "Doctor123!");
await SeedUserAsync("pharmacist@demo.clinic","Raj",      "Gunawardena",Roles.Pharmacist,   "Pharma123!");
await SeedUserAsync("reception@demo.clinic", "Anika",    "Fernando",   Roles.Receptionist, "Recep123!");

logger.LogInformation("DemoSeeder completed");

async Task SeedUserAsync(string email, string first, string last, string role, string password)
{
    var existing = await userMgr.FindByEmailAsync(email);
    if (existing is not null)
    {
        logger.LogInformation("User already exists: {Email}", email);
        return;
    }

    var user = new ClinicUser
    {
        Id        = Guid.NewGuid(),
        UserName  = email,
        Email     = email,
        FirstName = first,
        LastName  = last,
        ClinicId  = DemoClinicId,
        IsActive  = true,
        CreatedAt = TimeProvider.System.GetUtcNow(),
        EmailConfirmed = true,
    };

    var result = await userMgr.CreateAsync(user, password);
    if (!result.Succeeded)
    {
        logger.LogError("Failed to create {Email}: {Errors}", email,
            string.Join(", ", result.Errors.Select(e => e.Description)));
        return;
    }

    await userMgr.AddToRoleAsync(user, role);
    logger.LogInformation("Seeded {Role}: {Email}", role, email);
}

/// <summary>Static tenant context for the seeder — no HTTP request involved.</summary>
sealed class StaticTenantContext(Guid tenantId) : Core.ITenantContext
{
    public Guid TenantId => tenantId;
}
