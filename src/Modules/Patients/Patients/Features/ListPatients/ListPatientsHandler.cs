using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;
using Patients.Persistence;

namespace Patients.Features.ListPatients;

public sealed class ListPatientsHandler(IDbContextFactory<PatientsDbContext> dbFactory)
    : IRequestHandler<ListPatientsQuery, Result<ListPatientsResponse>>
{
    public async ValueTask<Result<ListPatientsResponse>> Handle(
        ListPatientsQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var q = db.Patients.AsNoTracking();

        var total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await q
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new PatientSummaryDto(
                p.Id,
                p.FirstName,
                p.LastName,
                p.DateOfBirth,
                p.ContactPhone,
                p.ConsentToDataProcessing,
                p.ConsentToCommunications,
                p.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<ListPatientsResponse>.Ok(new ListPatientsResponse(items, total));
    }
}
