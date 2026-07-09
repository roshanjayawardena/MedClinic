using Billing.Contracts;
using Billing.Domain;
using Billing.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Billing.Features.CreateInvoice;

public sealed class CreateInvoiceHandler(
    IDbContextFactory<BillingDbContext> dbFactory)
    : IRequestHandler<CreateInvoiceCommand, Result<CreateInvoiceResponse>>
{
    public async ValueTask<Result<CreateInvoiceResponse>> Handle(
        CreateInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var duplicate = await db.Invoices
            .AnyAsync(i => i.EncounterId == command.EncounterId, cancellationToken)
            .ConfigureAwait(false);

        if (duplicate)
            return Result<CreateInvoiceResponse>.Fail(
                new Error("Invoice.Duplicate", $"An invoice for encounter {command.EncounterId} already exists."));

        if (command.LineItems.Count == 0)
            return Result<CreateInvoiceResponse>.Fail(
                new Error("Invoice.NoLineItems", "At least one line item is required."));

        var lineItems = command.LineItems
            .Select(l => InvoiceLineItem.Create(l.Description, l.ProcedureCode, l.UnitPrice, l.Quantity))
            .ToList();

        // AppointmentId is not known at manual creation time — use empty Guid as placeholder.
        var invoice = Invoice.Create(command.PatientId, command.EncounterId, Guid.Empty, lineItems);

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CreateInvoiceResponse>.Ok(new CreateInvoiceResponse(invoice.Id, invoice.TotalAmount));
    }
}
