using Billing.Contracts;
using Billing.Contracts.Events;
using Billing.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Billing.Features.RecordPayment;

public sealed class RecordPaymentHandler(
    IDbContextFactory<BillingDbContext> dbFactory,
    IMediator mediator,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<RecordPaymentCommand, Result<RecordPaymentResponse>>
{
    public async ValueTask<Result<RecordPaymentResponse>> Handle(
        RecordPaymentCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var invoice = await db.Invoices
            .FirstOrDefaultAsync(i => i.Id == command.InvoiceId, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
            return Result<RecordPaymentResponse>.Fail(
                new Error("Invoice.NotFound", $"Invoice {command.InvoiceId} not found."));

        var now = timeProvider.GetUtcNow();
        var payResult = invoice.RecordPayment(command.PaymentMethod, now);
        if (payResult.IsFailure)
            return Result<RecordPaymentResponse>.Fail(payResult.Error!);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await mediator.Publish(
            new InvoicePaidIntegrationEvent(
                invoice.Id,
                invoice.PatientId,
                invoice.EncounterId,
                tenantContext.TenantId,
                invoice.TotalAmount,
                invoice.PaymentMethod!,
                invoice.PaidAt!.Value,
                now),
            cancellationToken).ConfigureAwait(false);

        return Result<RecordPaymentResponse>.Ok(new RecordPaymentResponse(invoice.Id, invoice.PaidAt!.Value));
    }
}
