using Appointments.Contracts;
using Appointments.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;

namespace Appointments.Features.ListAppointments;

public sealed class ListAppointmentsHandler(
    IDbContextFactory<AppointmentsDbContext> dbFactory,
    IMediator mediator)
    : IRequestHandler<ListAppointmentsQuery, Result<ListAppointmentsResponse>>
{
    public async ValueTask<Result<ListAppointmentsResponse>> Handle(
        ListAppointmentsQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var total = await db.Appointments.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await db.Appointments
            .AsNoTracking()
            .OrderByDescending(a => a.ScheduledAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(a => new { a.Id, a.PatientId, a.ScheduledAt, a.DurationMinutes, a.Reason, a.Status })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Batch-load distinct patient names via cross-module contract.
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
            return new AppointmentSummaryDto(
                r.Id,
                r.PatientId,
                first,
                last,
                r.ScheduledAt,
                r.DurationMinutes,
                r.Reason,
                r.Status.ToString());
        }).ToList();

        return Result<ListAppointmentsResponse>.Ok(new ListAppointmentsResponse(items, total));
    }
}
