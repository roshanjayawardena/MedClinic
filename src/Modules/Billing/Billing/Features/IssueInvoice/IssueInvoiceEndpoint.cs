using Billing.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Billing.Features.IssueInvoice;

internal static class IssueInvoiceEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/invoices/{id}/issue", Handle)
            .WithName("IssueInvoice")
            .WithTags("Billing")
            .WithSummary("Transition a Draft invoice to Issued status")
            .Produces<IssueInvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new IssueInvoiceCommand(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.Error!.Code] = [result.Error.Message],
            });
    }
}
