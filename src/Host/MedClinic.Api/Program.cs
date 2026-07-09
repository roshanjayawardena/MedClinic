using Appointments;
using Billing;
using Core;
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

builder.Services.AddMediator();
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "MediClinic API";
        options.Theme = ScalarTheme.Purple;
    });
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
