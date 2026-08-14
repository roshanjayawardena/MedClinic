using Clinics.Contracts;
using Clinics.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Clinics.Features.ListClinics;

public sealed class ListClinicsHandler(IDbContextFactory<ClinicsDbContext> dbFactory)
    : IRequestHandler<ListClinicsQuery, Result<ListClinicsResponse>>
{
    public async ValueTask<Result<ListClinicsResponse>> Handle(
        ListClinicsQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var q = db.Clinics.AsNoTracking();
        if (query.ActiveOnly)
            q = q.Where(c => c.IsActive);

        var total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await q
            .OrderBy(c => c.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new ClinicSummaryDto(c.Id, c.Name, c.Slug, c.Plan.ToString(), c.IsActive, c.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<ListClinicsResponse>.Ok(new ListClinicsResponse(items, total));
    }
}
