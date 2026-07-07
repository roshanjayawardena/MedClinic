using Encounters.Contracts;
using FluentValidation;

namespace Encounters.Features.RecordVitals;

public sealed class RecordVitalsValidator : AbstractValidator<RecordVitalsCommand>
{
    public RecordVitalsValidator()
    {
        RuleFor(c => c.EncounterId).NotEmpty();

        RuleFor(c => c.SystolicBp).InclusiveBetween(40, 300).When(c => c.SystolicBp.HasValue);
        RuleFor(c => c.DiastolicBp).InclusiveBetween(20, 200).When(c => c.DiastolicBp.HasValue);
        RuleFor(c => c.HeartRateBpm).InclusiveBetween(20, 300).When(c => c.HeartRateBpm.HasValue);
        RuleFor(c => c.TemperatureCelsius).InclusiveBetween(30m, 45m).When(c => c.TemperatureCelsius.HasValue);
        RuleFor(c => c.RespiratoryRatePerMin).InclusiveBetween(4, 60).When(c => c.RespiratoryRatePerMin.HasValue);
        RuleFor(c => c.OxygenSaturationPercent).InclusiveBetween(50, 100).When(c => c.OxygenSaturationPercent.HasValue);
        RuleFor(c => c.WeightKg).InclusiveBetween(0.5m, 700m).When(c => c.WeightKg.HasValue);

        RuleFor(c => c)
            .Must(c => c.SystolicBp.HasValue || c.DiastolicBp.HasValue || c.HeartRateBpm.HasValue
                    || c.TemperatureCelsius.HasValue || c.RespiratoryRatePerMin.HasValue
                    || c.OxygenSaturationPercent.HasValue || c.WeightKg.HasValue)
            .WithMessage("At least one vital sign must be provided.");
    }
}
