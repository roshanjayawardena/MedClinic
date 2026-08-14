using Core;
using Mediator;

namespace Patients.Contracts;

public sealed record ListPatientsQuery(int Page, int PageSize) : IRequest<Result<ListPatientsResponse>>;

public sealed record ListPatientsResponse(
    IReadOnlyList<PatientSummaryDto> Items,
    int TotalCount);

public sealed record PatientSummaryDto(
    Guid PatientId,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string ContactPhone,
    bool ConsentToDataProcessing,
    bool ConsentToCommunications,
    DateTimeOffset CreatedAt);
