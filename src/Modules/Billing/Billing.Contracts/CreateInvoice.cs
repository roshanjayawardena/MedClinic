using Core;
using Mediator;

namespace Billing.Contracts;

public sealed record CreateInvoiceCommand(
    Guid PatientId,
    Guid EncounterId,
    IReadOnlyList<CreateInvoiceLineItemDto> LineItems) : IRequest<Result<CreateInvoiceResponse>>;

public sealed record CreateInvoiceLineItemDto(
    string Description,
    string? ProcedureCode,
    decimal UnitPrice,
    int Quantity);

public sealed record CreateInvoiceResponse(Guid InvoiceId, decimal TotalAmount);
