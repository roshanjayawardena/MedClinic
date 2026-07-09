using Core;
using Mediator;

namespace Billing.Contracts;

public sealed record GetInvoiceByIdQuery(Guid InvoiceId) : IRequest<Result<GetInvoiceByIdResponse>>;

public sealed record GetInvoiceByIdResponse(
    Guid InvoiceId,
    Guid PatientId,
    Guid EncounterId,
    Guid AppointmentId,
    string Status,
    decimal TotalAmount,
    IReadOnlyList<InvoiceLineItemDto> LineItems,
    DateTimeOffset CreatedAt,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? PaidAt,
    string? PaymentMethod,
    DateTimeOffset? VoidedAt,
    string? VoidReason);

public sealed record InvoiceLineItemDto(
    string Description,
    string? ProcedureCode,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);
