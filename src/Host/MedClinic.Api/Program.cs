using Appointments;
using Billing;
using Core;
using Hangfire;
using Hangfire.PostgreSql;
using Notifications;
using Encounters;
using Identity;
using Identity.Middleware;
using MedClinic.Api;
using Microsoft.Extensions.DependencyInjection;
using Patients;
using Prescriptions;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ServiceLifetime.Scoped allows handlers to consume scoped dependencies (UserManager,
// ICurrentUser, INotificationSender). Singleton is Mediator's default but is incompatible
// with ASP.NET Core Identity services which are always registered as Scoped.
builder.Services.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITenantContext, HttpTenantContext>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOpenApi();

var modules = new IModule[]
{
    new PatientsModule(),
    new AppointmentsModule(),
    new EncountersModule(),
    new PrescriptionsModule(),
    new IdentityModule(),
    new BillingModule(),
    new NotificationsModule(),
};

foreach (var module in modules)
    module.RegisterServices(builder.Services, builder.Configuration);

// Hangfire — PostgreSQL-backed job storage + in-process server.
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(opts =>
        opts.UseNpgsqlConnection(builder.Configuration["ConnectionStrings:DefaultConnection"]!),
        new PostgreSqlStorageOptions { SchemaName = "hangfire" }));
builder.Services.AddHangfireServer();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "MediClinic API";
        options.Theme = ScalarTheme.Purple;
    });
    // Hangfire dashboard — dev-only; restrict to authenticated admins in production.
    app.UseHangfireDashboard("/hangfire");
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
// Validates JWT clinic_id == X-Tenant-Id header — prevents cross-clinic data access.
app.UseMiddleware<TenantClaimValidationMiddleware>();
app.UseAuthorization();

foreach (var module in modules)
    module.MapEndpoints(app);

app.Run();
