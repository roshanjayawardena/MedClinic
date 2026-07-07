using Core;
using Encounters.Features.AddDiagnosis;
using Encounters.Features.CloseEncounter;
using Encounters.Features.GetEncounterById;
using Encounters.Features.OpenEncounter;
using Encounters.Features.RecordVitals;
using Encounters.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: MedClinicModule(typeof(Encounters.EncountersModule), order: 30)]

namespace Encounters;

public sealed class EncountersModule : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<EncountersDbContext>((sp, options) =>
            options.UseNpgsql(
                configuration["ConnectionStrings:DefaultConnection"],
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "encounters")));

        services.AddValidatorsFromAssemblyContaining<OpenEncounterValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        OpenEncounterEndpoint.Map(app);
        AddDiagnosisEndpoint.Map(app);
        RecordVitalsEndpoint.Map(app);
        CloseEncounterEndpoint.Map(app);
        GetEncounterByIdEndpoint.Map(app);
    }
}
