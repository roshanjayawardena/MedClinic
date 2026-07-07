using Core;
using Mediator;

namespace Appointments.Contracts;

public sealed record GetAppointmentByIdQuery(Guid AppointmentId) : IRequest<Result<GetAppointmentByIdResponse>>;

public sealed record GetAppointmentByIdResponse(
    Guid AppointmentId,
    Guid PatientId,
    DateTimeOffset ScheduledAt,
    int DurationMinutes,
    string Reason,
    string Status,
    DateTimeOffset? CheckedInAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt);
