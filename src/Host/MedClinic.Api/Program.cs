using Core;
using MedClinic.Api;
using Patients;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPatientsModule(builder.Configuration);

var app = builder.Build();

app.MapPatientsEndpoints();

app.Run();
