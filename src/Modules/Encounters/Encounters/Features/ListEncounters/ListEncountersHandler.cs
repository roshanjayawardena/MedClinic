using Core;
using Encounters.Contracts;
using Encounters.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;

namespace Encounters.Features.ListEncounters;

public sealed class ListEncountersHandler(
    IDbContextFactory<EncountersDbContext> dbFactory,
    IMediator mediator)
    : IRequestHandler<ListEncountersQuery, Result<ListEncountersResponse>>
{
    public async ValueTask<Result<ListEncountersResponse>> Handle(
        ListEncountersQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var total = await db.Encounters.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await db.Encounters
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(e => new { e.Id, e.PatientId, e.Status, e.CreatedAt, e.ClosedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var patientIds = rows.Select(r => r.PatientId).Distinct().ToList();
        var nameMap = new Dictionary<Guid, (string First, string Last)>();

        foreach (var pid in patientIds)
        {
            var result = await mediator.Send(new GetPatientByIdQuery(pid), cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
                nameMap[pid] = (result.Value.FirstName, result.Value.LastName);
        }

        var items = rows.Select(r =>
        {
            var (first, last) = nameMap.TryGetValue(r.PatientId, out var n) ? n : ("Unknown", "");
            return new EncounterListItemDto(r.Id, r.PatientId, first, last, r.Status.ToString(), r.CreatedAt, r.ClosedAt);
        }).ToList();

        return Result<ListEncountersResponse>.Ok(new ListEncountersResponse(items, total));
    }
}
