using Core;
using Encounters.Contracts;
using Encounters.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Encounters.Features.ListEncountersByPatient;

public sealed class ListEncountersByPatientHandler(IDbContextFactory<EncountersDbContext> dbFactory)
    : IRequestHandler<ListEncountersByPatientQuery, Result<IReadOnlyList<EncounterSummaryDto>>>
{
    public async ValueTask<Result<IReadOnlyList<EncounterSummaryDto>>> Handle(
        ListEncountersByPatientQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var encounters = await db.Encounters
            .AsNoTracking()
            .Where(e => e.PatientId == query.PatientId)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new EncounterSummaryDto(
                e.Id,
                e.PatientId,
                e.Status.ToString(),
                e.CreatedAt,
                e.ClosedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<EncounterSummaryDto>>.Ok(encounters);
    }
}
