using Core;
using Mediator;

namespace Billing.Contracts;

public sealed record VoidInvoiceCommand(Guid InvoiceId, string Reason) : IRequest<Result<VoidInvoiceResponse>>;

public sealed record VoidInvoiceResponse(Guid InvoiceId);
