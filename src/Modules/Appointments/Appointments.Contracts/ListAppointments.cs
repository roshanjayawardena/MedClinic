using Core;
using Mediator;

namespace Appointments.Contracts;

public sealed record ListAppointmentsQuery(int Page, int PageSize)
    : IRequest<Result<ListAppointmentsResponse>>;

public sealed record ListAppointmentsResponse(
    IReadOnlyList<AppointmentSummaryDto> Items,
    int TotalCount);

public sealed record AppointmentSummaryDto(
    Guid AppointmentId,
    Guid PatientId,
    string PatientFirstName,
    string PatientLastName,
    DateTimeOffset ScheduledAt,
    int DurationMinutes,
    string Reason,
    string Status);
