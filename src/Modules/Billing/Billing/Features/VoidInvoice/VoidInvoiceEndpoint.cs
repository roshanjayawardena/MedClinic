using Billing.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Billing.Features.VoidInvoice;

internal static class VoidInvoiceEndpoint
{
    internal sealed record VoidInvoiceRequest(string Reason);

    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/invoices/{id}/void", Handle)
            .WithName("VoidInvoice")
            .WithTags("Billing")
            .WithSummary("Void a Draft or Issued invoice")
            .Produces<VoidInvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Guid id,
        VoidInvoiceRequest body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new VoidInvoiceCommand(id, body.Reason), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.Error!.Code] = [result.Error.Message],
            });
    }
}
