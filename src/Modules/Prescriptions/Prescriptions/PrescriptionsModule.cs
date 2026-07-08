using Core;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prescriptions.Features.ActivatePrescription;
using Prescriptions.Features.DispensePrescription;
using Prescriptions.Features.GetPrescriptionById;
using Prescriptions.Features.WritePrescription;
using Prescriptions.Persistence;

[assembly: MedClinicModule(typeof(Prescriptions.PrescriptionsModule), order: 40)]

namespace Prescriptions;

public sealed class PrescriptionsModule : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<PrescriptionsDbContext>((sp, options) =>
            options.UseNpgsql(
                configuration["ConnectionStrings:DefaultConnection"],
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "prescriptions")));

        services.AddValidatorsFromAssemblyContaining<WritePrescriptionValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        WritePrescriptionEndpoint.Map(app);
        ActivatePrescriptionEndpoint.Map(app);
        DispensePrescriptionEndpoint.Map(app);
        GetPrescriptionByIdEndpoint.Map(app);
    }
}
