using Billing.Contracts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Billing.Features.RecordPayment;

internal static class RecordPaymentEndpoint
{
    internal sealed record RecordPaymentRequest(string PaymentMethod);

    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/invoices/{id}/pay", Handle)
            .WithName("RecordPayment")
            .WithTags("Billing")
            .WithSummary("Mark an Issued invoice as Paid")
            .Produces<RecordPaymentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Guid id,
        RecordPaymentRequest body,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RecordPaymentCommand(id, body.PaymentMethod), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.Error!.Code] = [result.Error.Message],
            });
    }
}
