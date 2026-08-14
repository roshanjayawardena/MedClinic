using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;
using Prescriptions.Contracts;
using Prescriptions.Domain;
using Prescriptions.Persistence;

namespace Prescriptions.Features.ListPrescriptions;

public sealed class ListPrescriptionsHandler(
    IDbContextFactory<PrescriptionsDbContext> dbFactory,
    IMediator mediator)
    : IRequestHandler<ListPrescriptionsQuery, Result<ListPrescriptionsResponse>>
{
    public async ValueTask<Result<ListPrescriptionsResponse>> Handle(
        ListPrescriptionsQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var total = await db.Prescriptions.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await db.Prescriptions
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new
            {
                p.Id, p.EncounterId, p.PatientId, p.DrugName,
                p.DosageInstructions, p.QuantityDays, p.Status,
                p.ActivatedAt, p.DispensedAt, p.CreatedAt,
            })
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
            return new PrescriptionSummaryDto(
                r.Id, r.EncounterId, r.PatientId, first, last,
                r.DrugName, r.DosageInstructions, r.QuantityDays,
                r.Status.ToString(), r.ActivatedAt, r.DispensedAt, r.CreatedAt);
        }).ToList();

        return Result<ListPrescriptionsResponse>.Ok(new ListPrescriptionsResponse(items, total));
    }
}
