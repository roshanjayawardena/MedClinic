using Core;
using Mediator;

namespace Billing.Contracts;

public sealed record IssueInvoiceCommand(Guid InvoiceId) : IRequest<Result<IssueInvoiceResponse>>;

public sealed record IssueInvoiceResponse(Guid InvoiceId, DateTimeOffset IssuedAt);
