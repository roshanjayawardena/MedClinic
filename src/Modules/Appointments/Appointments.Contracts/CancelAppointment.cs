using Core;
using Mediator;

namespace Appointments.Contracts;

public sealed record CancelAppointmentCommand(
    Guid AppointmentId,
    string Reason) : IRequest<Result<CancelAppointmentResponse>>;

public sealed record CancelAppointmentResponse(Guid AppointmentId, string Status);
