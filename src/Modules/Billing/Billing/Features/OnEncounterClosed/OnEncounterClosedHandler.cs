using Billing.Domain;
using Billing.Persistence;
using Encounters.Contracts.Events;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Billing.Features.OnEncounterClosed;

/// <summary>
/// Creates a Draft invoice automatically when a clinical encounter is closed.
/// Idempotent: a second delivery of the same event is a no-op.
/// The consultation fee amount is a clinic-wide constant; configurable per clinic in a future billing settings module.
/// </summary>
public sealed class OnEncounterClosedHandler(IDbContextFactory<BillingDbContext> dbFactory)
    : INotificationHandler<EncounterClosedIntegrationEvent>
{
    private const decimal ConsultationFee = 150.00m;

    public async ValueTask Handle(
        EncounterClosedIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Idempotency guard — one invoice per encounter, at-least-once delivery assumption.
        var alreadyExists = await db.Invoices
            .AnyAsync(i => i.EncounterId == notification.EncounterId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyExists)
            return;

        var consultationItem = InvoiceLineItem.Create(
            description: "Consultation fee",
            procedureCode: null,
            unitPrice: ConsultationFee,
            quantity: 1);

        var invoice = Invoice.Create(
            notification.PatientId,
            notification.EncounterId,
            notification.AppointmentId,
            [consultationItem]);

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
