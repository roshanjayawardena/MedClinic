using Billing.Contracts;
using FluentValidation;

namespace Billing.Features.CreateInvoice;

public sealed class CreateInvoiceValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.EncounterId).NotEmpty();
        RuleFor(x => x.LineItems).NotEmpty().WithMessage("At least one line item is required.");

        RuleForEach(x => x.LineItems).ChildRules(item =>
        {
            item.RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
            item.RuleFor(x => x.UnitPrice).GreaterThan(0);
            item.RuleFor(x => x.Quantity).GreaterThan(0);
            item.RuleFor(x => x.ProcedureCode).MaximumLength(20).When(x => x.ProcedureCode is not null);
        });
    }
}
