using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;
using Patients.Persistence;

namespace Patients.Features.GetPatientAllergies;

public sealed class GetPatientAllergiesHandler(IDbContextFactory<PatientsDbContext> dbFactory)
    : IRequestHandler<GetPatientAllergiesQuery, Result<GetPatientAllergiesResponse>>
{
    public async ValueTask<Result<GetPatientAllergiesResponse>> Handle(
        GetPatientAllergiesQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var allergies = await db.Allergies
            .AsNoTracking()
            .Where(a => a.PatientId == query.PatientId)
            .Select(a => new PatientAllergyDto(a.DrugName, a.Severity))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<GetPatientAllergiesResponse>.Ok(new GetPatientAllergiesResponse(allergies));
    }
}
