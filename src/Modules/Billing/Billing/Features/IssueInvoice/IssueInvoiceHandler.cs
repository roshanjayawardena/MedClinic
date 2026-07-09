using Billing.Contracts;
using Billing.Domain;
using Billing.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Billing.Features.IssueInvoice;

public sealed class IssueInvoiceHandler(
    IDbContextFactory<BillingDbContext> dbFactory,
    TimeProvider timeProvider)
    : IRequestHandler<IssueInvoiceCommand, Result<IssueInvoiceResponse>>
{
    public async ValueTask<Result<IssueInvoiceResponse>> Handle(
        IssueInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var invoice = await db.Invoices
            .FirstOrDefaultAsync(i => i.Id == command.InvoiceId, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
            return Result<IssueInvoiceResponse>.Fail(
                new Error("Invoice.NotFound", $"Invoice {command.InvoiceId} not found."));

        var now = timeProvider.GetUtcNow();
        var issueResult = invoice.Issue(now);
        if (issueResult.IsFailure)
            return Result<IssueInvoiceResponse>.Fail(issueResult.Error!);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<IssueInvoiceResponse>.Ok(new IssueInvoiceResponse(invoice.Id, invoice.IssuedAt!.Value));
    }
}
