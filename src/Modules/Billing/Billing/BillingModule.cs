using Billing.Features.CreateInvoice;
using Billing.Features.GetInvoiceById;
using Billing.Features.IssueInvoice;
using Billing.Features.RecordPayment;
using Billing.Features.VoidInvoice;
using Billing.Persistence;
using Core;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: MedClinicModule(typeof(Billing.BillingModule), order: 60)]

namespace Billing;

public sealed class BillingModule : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<BillingDbContext>((sp, options) =>
            options.UseNpgsql(
                configuration["ConnectionStrings:DefaultConnection"],
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "billing")));

        services.AddValidatorsFromAssemblyContaining<CreateInvoiceValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        CreateInvoiceEndpoint.Map(app);
        IssueInvoiceEndpoint.Map(app);
        RecordPaymentEndpoint.Map(app);
        VoidInvoiceEndpoint.Map(app);
        GetInvoiceByIdEndpoint.Map(app);
    }
}
