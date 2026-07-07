using Core;
using MedClinic.Api;
using Microsoft.Extensions.DependencyInjection;
using Patients;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITenantContext, HttpTenantContext>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOpenApi();

var patientsModule = new PatientsModule();
patientsModule.RegisterServices(builder.Services, builder.Configuration);

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

patientsModule.MapEndpoints(app);

app.Run();
