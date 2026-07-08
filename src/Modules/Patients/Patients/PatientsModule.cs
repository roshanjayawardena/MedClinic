using Core;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Patients.Features.AddAllergy;
using Patients.Features.GetPatientAllergies;
using Patients.Features.GetPatientById;
using Patients.Features.RegisterPatient;
using Patients.Persistence;

[assembly: MedClinicModule(typeof(Patients.PatientsModule), order: 10)]

namespace Patients;

public sealed class PatientsModule : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<PatientsDbContext>((sp, options) =>
            options.UseNpgsql(
                configuration["ConnectionStrings:DefaultConnection"],
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "patients")));

        services.AddValidatorsFromAssemblyContaining<RegisterPatientValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RegisterPatientEndpoint.Map(app);
        GetPatientByIdEndpoint.Map(app);
        AddAllergyEndpoint.Map(app);
        GetPatientAllergiesEndpoint.Map(app);
    }
}
