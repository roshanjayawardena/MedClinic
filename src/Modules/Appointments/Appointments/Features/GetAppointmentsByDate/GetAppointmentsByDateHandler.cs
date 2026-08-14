using Appointments.Contracts;
using Appointments.Persistence;
using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;

namespace Appointments.Features.GetAppointmentsByDate;

public sealed class GetAppointmentsByDateHandler(
    IDbContextFactory<AppointmentsDbContext> dbFactory,
    IMediator mediator)
    : IRequestHandler<GetAppointmentsByDateQuery, Result<GetAppointmentsByDateResponse>>
{
    public async ValueTask<Result<GetAppointmentsByDateResponse>> Handle(
        GetAppointmentsByDateQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var start = new DateTimeOffset(query.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end   = start.AddDays(1);

        var rows = await db.Appointments
            .AsNoTracking()
            .Where(a => a.ScheduledAt >= start && a.ScheduledAt < end)
            .OrderBy(a => a.ScheduledAt)
            .Select(a => new { a.Id, a.PatientId, a.ScheduledAt, a.DurationMinutes, a.Reason, a.Status })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var patientIds = rows.Select(r => r.PatientId).Distinct().ToList();
        var nameMap    = new Dictionary<Guid, (string First, string Last)>();

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
                r.Id, r.PatientId, first, last,
                r.ScheduledAt, r.DurationMinutes, r.Reason, r.Status.ToString());
        }).ToList();

        return Result<GetAppointmentsByDateResponse>.Ok(new GetAppointmentsByDateResponse(items));
    }
}
