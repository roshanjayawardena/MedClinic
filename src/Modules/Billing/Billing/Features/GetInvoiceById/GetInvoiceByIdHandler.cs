using Billing.Contracts;
using Billing.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Billing.Features.GetInvoiceById;

public sealed class GetInvoiceByIdHandler(IDbContextFactory<BillingDbContext> dbFactory)
    : IRequestHandler<GetInvoiceByIdQuery, Result<GetInvoiceByIdResponse>>
{
    public async ValueTask<Result<GetInvoiceByIdResponse>> Handle(
        GetInvoiceByIdQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var invoice = await db.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == query.InvoiceId, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
            return Result<GetInvoiceByIdResponse>.Fail(
                new Error("Invoice.NotFound", $"Invoice {query.InvoiceId} not found."));

        var lineItemDtos = invoice.LineItems
            .Select(l => new InvoiceLineItemDto(l.Description, l.ProcedureCode, l.UnitPrice, l.Quantity, l.LineTotal))
            .ToList();

        return Result<GetInvoiceByIdResponse>.Ok(new GetInvoiceByIdResponse(
            invoice.Id,
            invoice.PatientId,
            invoice.EncounterId,
            invoice.AppointmentId,
            invoice.Status.ToString(),
            invoice.TotalAmount,
            lineItemDtos,
            invoice.CreatedAt,
            invoice.IssuedAt,
            invoice.PaidAt,
            invoice.PaymentMethod,
            invoice.VoidedAt,
            invoice.VoidReason));
    }
}
