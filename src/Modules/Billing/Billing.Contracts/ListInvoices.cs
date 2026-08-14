using Core;
using Mediator;

namespace Billing.Contracts;

public sealed record ListInvoicesQuery(int Page, int PageSize)
    : IRequest<Result<ListInvoicesResponse>>;

public sealed record ListInvoicesResponse(
    IReadOnlyList<InvoiceSummaryDto> Items,
    int TotalCount);

public sealed record InvoiceSummaryDto(
    Guid InvoiceId,
    Guid PatientId,
    Guid EncounterId,
    string PatientFirstName,
    string PatientLastName,
    string Status,
    decimal TotalAmount,
    IReadOnlyList<InvoiceLineItemDto> LineItems,
    DateTimeOffset CreatedAt,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? PaidAt,
    string? PaymentMethod);
