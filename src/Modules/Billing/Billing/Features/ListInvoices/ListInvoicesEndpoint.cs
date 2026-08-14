using Billing.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Billing.Features.ListInvoices;

internal static class ListInvoicesEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/invoices", Handle)
            .WithName("ListInvoices")
            .WithTags("Billing")
            .WithSummary("List all invoices (paged)")
            .Produces<ListInvoicesResponse>()
            .RequireAuthorization();
    }

    private static async Task<IResult> Handle(
        int page,
        int pageSize,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListInvoicesQuery(page, pageSize), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem("Failed to list invoices");
    }
}
