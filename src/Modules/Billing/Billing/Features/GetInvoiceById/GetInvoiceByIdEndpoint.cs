using Billing.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Billing.Features.GetInvoiceById;

internal static class GetInvoiceByIdEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/invoices/{id}", Handle)
            .WithName("GetInvoiceById")
            .WithTags("Billing")
            .WithSummary("Get an invoice by ID")
            .Produces<GetInvoiceByIdResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetInvoiceByIdQuery(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound(new { result.Error!.Code, result.Error.Message });
    }
}
