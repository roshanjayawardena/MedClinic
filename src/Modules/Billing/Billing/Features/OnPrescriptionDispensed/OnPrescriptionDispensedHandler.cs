using Billing.Domain;
using Billing.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Prescriptions.Contracts.Events;

namespace Billing.Features.OnPrescriptionDispensed;

/// <summary>
/// Adds a dispensing line item to the encounter invoice when a prescription is dispensed.
/// Idempotent: a second delivery for the same PrescriptionId is a no-op.
/// No-op if the encounter invoice does not yet exist (encounter not yet closed).
/// No PHI is logged — drug names and patient identifiers are never written to structured logs.
/// </summary>
public sealed class OnPrescriptionDispensedHandler(IDbContextFactory<BillingDbContext> dbFactory)
    : INotificationHandler<PrescriptionDispensedIntegrationEvent>
{
    private const decimal DispensingFee = 25.00m;

    public async ValueTask Handle(
        PrescriptionDispensedIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var invoice = await db.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.EncounterId == notification.EncounterId, cancellationToken)
            .ConfigureAwait(false);

        // No invoice yet (encounter not closed) or already has a dispensing line for this prescription.
        if (invoice is null)
            return;

        var alreadyAdded = invoice.LineItems
            .Any(li => li.Description == $"Dispensing fee [{notification.PrescriptionId}]");

        if (alreadyAdded)
            return;

        var dispensingItem = InvoiceLineItem.Create(
            description: $"Dispensing fee [{notification.PrescriptionId}]",
            procedureCode: null,
            unitPrice: DispensingFee,
            quantity: 1);

        invoice.AddLineItem(dispensingItem);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
