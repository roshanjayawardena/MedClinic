using Core;
using Mediator;

namespace Prescriptions.Contracts;

public sealed record ListPrescriptionsQuery(int Page, int PageSize)
    : IRequest<Result<ListPrescriptionsResponse>>;

public sealed record ListPrescriptionsResponse(
    IReadOnlyList<PrescriptionSummaryDto> Items,
    int TotalCount);

public sealed record PrescriptionSummaryDto(
    Guid PrescriptionId,
    Guid EncounterId,
    Guid PatientId,
    string PatientFirstName,
    string PatientLastName,
    string DrugName,
    string DosageInstructions,
    int QuantityDays,
    string Status,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DispensedAt,
    DateTimeOffset CreatedAt);
