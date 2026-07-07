using Core;
using Mediator;

namespace Appointments.Contracts;

public sealed record BookAppointmentCommand(
    Guid PatientId,
    DateTimeOffset ScheduledAt,
    int DurationMinutes,
    string Reason) : IRequest<Result<BookAppointmentResponse>>;

public sealed record BookAppointmentResponse(Guid AppointmentId);
