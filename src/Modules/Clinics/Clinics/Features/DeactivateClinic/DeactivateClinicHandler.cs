using Clinics.Contracts;
using Clinics.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Clinics.Features.DeactivateClinic;

public sealed class DeactivateClinicHandler(
    IDbContextFactory<ClinicsDbContext> dbFactory,
    TimeProvider timeProvider)
    : IRequestHandler<DeactivateClinicCommand, Result<DeactivateClinicResponse>>
{
    public async ValueTask<Result<DeactivateClinicResponse>> Handle(
        DeactivateClinicCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var clinic = await db.Clinics
            .FirstOrDefaultAsync(c => c.Id == command.ClinicId, cancellationToken)
            .ConfigureAwait(false);

        if (clinic is null)
            return Result<DeactivateClinicResponse>.Fail(
                new Error("Clinic.NotFound", "Clinic not found."));

        var result = clinic.Deactivate(timeProvider.GetUtcNow());
        if (result.IsFailure)
            return Result<DeactivateClinicResponse>.Fail(result.Error!);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<DeactivateClinicResponse>.Ok(
            new DeactivateClinicResponse(clinic.Id, clinic.DeactivatedAt!.Value));
    }
}
