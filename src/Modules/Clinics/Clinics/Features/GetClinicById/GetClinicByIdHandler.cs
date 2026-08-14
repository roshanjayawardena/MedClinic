using Clinics.Contracts;
using Clinics.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Clinics.Features.GetClinicById;

public sealed class GetClinicByIdHandler(IDbContextFactory<ClinicsDbContext> dbFactory)
    : IRequestHandler<GetClinicByIdQuery, Result<GetClinicByIdResponse>>
{
    public async ValueTask<Result<GetClinicByIdResponse>> Handle(
        GetClinicByIdQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var clinic = await db.Clinics
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.ClinicId, cancellationToken)
            .ConfigureAwait(false);

        return clinic is null
            ? Result<GetClinicByIdResponse>.Fail(new Error("Clinic.NotFound", "Clinic not found."))
            : Result<GetClinicByIdResponse>.Ok(new GetClinicByIdResponse(
                clinic.Id, clinic.Name, clinic.Slug, clinic.ContactEmail,
                clinic.TimeZoneId, clinic.Plan.ToString(),
                clinic.IsActive, clinic.CreatedAt, clinic.DeactivatedAt));
    }
}
