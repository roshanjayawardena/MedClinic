using Clinics.Features.DeactivateClinic;
using Clinics.Features.GetClinicById;
using Clinics.Features.ListClinics;
using Clinics.Features.RegisterClinic;
using Clinics.Persistence;
using Core;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: MedClinicModule(typeof(Clinics.ClinicsModule), order: 5)]

namespace Clinics;

public sealed class ClinicsModule : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<ClinicsDbContext>((_, options) =>
            options.UseNpgsql(
                configuration["ConnectionStrings:DefaultConnection"],
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "clinics")));

        services.AddValidatorsFromAssemblyContaining<RegisterClinicValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RegisterClinicEndpoint.Map(app);
        GetClinicByIdEndpoint.Map(app);
        ListClinicsEndpoint.Map(app);
        DeactivateClinicEndpoint.Map(app);
    }
}
