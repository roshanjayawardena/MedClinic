using Core;
using Mediator;

namespace Appointments.Contracts;

public sealed record CheckInAppointmentCommand(Guid AppointmentId) : IRequest<Result<CheckInAppointmentResponse>>;

public sealed record CheckInAppointmentResponse(Guid AppointmentId, string Status);
