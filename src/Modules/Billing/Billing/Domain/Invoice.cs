using Core;

namespace Billing.Domain;

public sealed class Invoice : AuditableEntity
{
    private List<InvoiceLineItem> _lineItems = [];

    private Invoice() { }

    public Guid PatientId { get; private set; }
    public Guid EncounterId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public IReadOnlyList<InvoiceLineItem> LineItems => _lineItems.AsReadOnly();
    public DateTimeOffset? IssuedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? PaymentMethod { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    public static Invoice Create(
        Guid patientId,
        Guid encounterId,
        Guid appointmentId,
        IEnumerable<InvoiceLineItem> lineItems)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            EncounterId = encounterId,
            AppointmentId = appointmentId,
            Status = InvoiceStatus.Draft,
        };

        invoice._lineItems.AddRange(lineItems);
        invoice.TotalAmount = invoice._lineItems.Sum(l => l.LineTotal);
        return invoice;
    }

    public Result Issue(DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Fail(new Error("Invoice.InvalidStatus",
                $"Only a Draft invoice can be issued. Current status: {Status}."));

        if (_lineItems.Count == 0)
            return Result.Fail(new Error("Invoice.NoLineItems",
                "Cannot issue an invoice with no line items."));

        Status = InvoiceStatus.Issued;
        IssuedAt = now;
        return Result.Ok();
    }

    public Result RecordPayment(string paymentMethod, DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Issued)
            return Result.Fail(new Error("Invoice.InvalidStatus",
                $"Only an Issued invoice can be marked paid. Current status: {Status}."));

        Status = InvoiceStatus.Paid;
        PaymentMethod = paymentMethod;
        PaidAt = now;
        return Result.Ok();
    }

    public Result Void(string reason, DateTimeOffset now)
    {
        if (Status == InvoiceStatus.Paid)
            return Result.Fail(new Error("Invoice.InvalidStatus",
                "A paid invoice cannot be voided."));

        if (Status == InvoiceStatus.Void)
            return Result.Fail(new Error("Invoice.InvalidStatus",
                "Invoice is already voided."));

        Status = InvoiceStatus.Void;
        VoidReason = reason;
        VoidedAt = now;
        return Result.Ok();
    }
}
