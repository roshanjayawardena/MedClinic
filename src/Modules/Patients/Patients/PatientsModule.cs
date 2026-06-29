using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Patients;

public static class PatientsModule
{
    public static IServiceCollection AddPatientsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PatientsDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PatientsDb")));

        services.AddValidatorsFromAssemblyContaining<RegisterPatientValidator>();
        services.AddMediator();

        return services;
    }
}
