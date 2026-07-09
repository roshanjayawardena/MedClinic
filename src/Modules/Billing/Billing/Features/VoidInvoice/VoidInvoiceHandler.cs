using Billing.Contracts;
using Billing.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Billing.Features.VoidInvoice;

public sealed class VoidInvoiceHandler(
    IDbContextFactory<BillingDbContext> dbFactory,
    TimeProvider timeProvider)
    : IRequestHandler<VoidInvoiceCommand, Result<VoidInvoiceResponse>>
{
    public async ValueTask<Result<VoidInvoiceResponse>> Handle(
        VoidInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var invoice = await db.Invoices
            .FirstOrDefaultAsync(i => i.Id == command.InvoiceId, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
            return Result<VoidInvoiceResponse>.Fail(
                new Error("Invoice.NotFound", $"Invoice {command.InvoiceId} not found."));

        var voidResult = invoice.Void(command.Reason, timeProvider.GetUtcNow());
        if (voidResult.IsFailure)
            return Result<VoidInvoiceResponse>.Fail(voidResult.Error!);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<VoidInvoiceResponse>.Ok(new VoidInvoiceResponse(invoice.Id));
    }
}
