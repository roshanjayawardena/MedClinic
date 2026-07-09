namespace Billing.Domain;

public sealed class InvoiceLineItem
{
    private InvoiceLineItem() { }

    public string Description { get; private set; } = string.Empty;
    public string? ProcedureCode { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }

    // Computed in C# — not mapped to a DB column.
    public decimal LineTotal => UnitPrice * Quantity;

    public static InvoiceLineItem Create(
        string description,
        string? procedureCode,
        decimal unitPrice,
        int quantity) =>
        new()
        {
            Description = description,
            ProcedureCode = procedureCode,
            UnitPrice = unitPrice,
            Quantity = quantity,
        };
}
