using Billing.Contracts;
using Billing.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;

namespace Billing.Features.ListInvoices;

public sealed class ListInvoicesHandler(
    IDbContextFactory<BillingDbContext> dbFactory,
    IMediator mediator)
    : IRequestHandler<ListInvoicesQuery, Result<ListInvoicesResponse>>
{
    public async ValueTask<Result<ListInvoicesResponse>> Handle(
        ListInvoicesQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var total = await db.Invoices.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await db.Invoices
            .AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var patientIds = rows.Select(r => r.PatientId).Distinct().ToList();
        var nameMap = new Dictionary<Guid, (string First, string Last)>();

        foreach (var pid in patientIds)
        {
            var result = await mediator.Send(new GetPatientByIdQuery(pid), cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
                nameMap[pid] = (result.Value.FirstName, result.Value.LastName);
        }

        var items = rows.Select(r =>
        {
            var (first, last) = nameMap.TryGetValue(r.PatientId, out var n) ? n : ("Unknown", "");
            var lineItems = r.LineItems
                .Select(l => new InvoiceLineItemDto(l.Description, l.ProcedureCode, l.UnitPrice, l.Quantity, l.LineTotal))
                .ToList();

            return new InvoiceSummaryDto(
                r.Id, r.PatientId, r.EncounterId, first, last,
                r.Status.ToString(), r.TotalAmount, lineItems,
                r.CreatedAt, r.IssuedAt, r.PaidAt, r.PaymentMethod);
        }).ToList();

        return Result<ListInvoicesResponse>.Ok(new ListInvoicesResponse(items, total));
    }
}
