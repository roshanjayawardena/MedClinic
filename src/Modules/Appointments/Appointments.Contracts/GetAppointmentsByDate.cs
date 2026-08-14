using Core;
using Mediator;

namespace Appointments.Contracts;

public sealed record GetAppointmentsByDateQuery(DateOnly Date)
    : IRequest<Result<GetAppointmentsByDateResponse>>;

public sealed record GetAppointmentsByDateResponse(IReadOnlyList<AppointmentSummaryDto> Items);
