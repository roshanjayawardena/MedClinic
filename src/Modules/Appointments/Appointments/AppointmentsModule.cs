using Appointments.Features.BookAppointment;
using Appointments.Features.CancelAppointment;
using Appointments.Features.CheckInAppointment;
using Appointments.Features.GetAppointmentById;
using Appointments.Persistence;
using Core;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: MedClinicModule(typeof(Appointments.AppointmentsModule), order: 20)]

namespace Appointments;

public sealed class AppointmentsModule : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<AppointmentsDbContext>((sp, options) =>
            options.UseNpgsql(
                configuration["ConnectionStrings:DefaultConnection"],
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "appointments")));

        services.AddValidatorsFromAssemblyContaining<BookAppointmentValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        BookAppointmentEndpoint.Map(app);
        CheckInAppointmentEndpoint.Map(app);
        CancelAppointmentEndpoint.Map(app);
        GetAppointmentByIdEndpoint.Map(app);
    }
}
