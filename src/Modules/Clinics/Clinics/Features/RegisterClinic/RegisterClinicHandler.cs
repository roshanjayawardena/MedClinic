using Clinics.Contracts;
using Clinics.Domain;
using Clinics.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Clinics.Features.RegisterClinic;

public sealed class RegisterClinicHandler(
    IDbContextFactory<ClinicsDbContext> dbFactory,
    TimeProvider timeProvider)
    : IRequestHandler<RegisterClinicCommand, Result<RegisterClinicResponse>>
{
    public async ValueTask<Result<RegisterClinicResponse>> Handle(
        RegisterClinicCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var slug = command.Slug.ToLowerInvariant();
        var slugTaken = await db.Clinics
            .AnyAsync(c => c.Slug == slug, cancellationToken)
            .ConfigureAwait(false);

        if (slugTaken)
            return Result<RegisterClinicResponse>.Fail(
                new Error("Clinic.SlugTaken", $"The slug '{command.Slug}' is already in use."));

        if (!Enum.TryParse<ClinicPlan>(command.Plan, out var plan))
            return Result<RegisterClinicResponse>.Fail(
                new Error("Clinic.InvalidPlan", $"Unknown plan '{command.Plan}'."));

        var clinic = Clinic.Register(
            command.Name,
            slug,
            command.ContactEmail,
            command.TimeZoneId,
            plan,
            timeProvider.GetUtcNow());

        db.Clinics.Add(clinic);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<RegisterClinicResponse>.Ok(
            new RegisterClinicResponse(clinic.Id, clinic.Name, clinic.Slug, clinic.Plan.ToString()));
    }
}
