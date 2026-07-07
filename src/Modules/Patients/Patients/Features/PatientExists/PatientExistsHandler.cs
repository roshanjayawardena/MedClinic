using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;
using Patients.Persistence;

namespace Patients.Features.PatientExists;

public sealed class PatientExistsHandler(IDbContextFactory<PatientsDbContext> dbFactory)
    : IRequestHandler<PatientExistsQuery, Result<bool>>
{
    public async ValueTask<Result<bool>> Handle(
        PatientExistsQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var exists = await db.Patients
            .AsNoTracking()
            .AnyAsync(p => p.Id == query.PatientId, cancellationToken)
            .ConfigureAwait(false);

        return Result<bool>.Ok(exists);
    }
}
