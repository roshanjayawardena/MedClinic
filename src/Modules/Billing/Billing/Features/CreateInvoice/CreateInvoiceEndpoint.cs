using Billing.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Web;

namespace Billing.Features.CreateInvoice;

internal static class CreateInvoiceEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/invoices", Handle)
            .WithName("CreateInvoice")
            .WithTags("Billing")
            .WithSummary("Create a draft invoice manually")
            .AddEndpointFilter<ValidationFilter<CreateInvoiceCommand>>()
            .Produces<CreateInvoiceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        CreateInvoiceCommand command,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/invoices/{result.Value.InvoiceId}", result.Value)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.Error!.Code] = [result.Error.Message],
            });
    }
}
