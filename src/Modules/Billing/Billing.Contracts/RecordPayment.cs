using Core;
using Mediator;

namespace Billing.Contracts;

public sealed record RecordPaymentCommand(
    Guid InvoiceId,
    string PaymentMethod) : IRequest<Result<RecordPaymentResponse>>;

public sealed record RecordPaymentResponse(Guid InvoiceId, DateTimeOffset PaidAt);
