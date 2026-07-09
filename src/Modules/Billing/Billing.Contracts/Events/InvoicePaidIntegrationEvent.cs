using Mediator;

namespace Billing.Contracts.Events;

public sealed record InvoicePaidIntegrationEvent(
    Guid InvoiceId,
    Guid PatientId,
    Guid EncounterId,
    Guid ClinicId,
    decimal AmountPaid,
    string PaymentMethod,
    DateTimeOffset PaidAt,
    DateTimeOffset OccurredAt) : INotification;
