using Core;
using Mediator;
using Patients.Contracts;

namespace Patients;

public sealed class RegisterPatientHandler(
    PatientsDbContext dbContext,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : IRequestHandler<RegisterPatientCommand, Result<RegisterPatientResponse>>
{
    public async ValueTask<Result<RegisterPatientResponse>> Handle(
        RegisterPatientCommand command,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();

        var patient = Patient.Register(
            tenantContext.TenantId,
            command.FirstName,
            command.LastName,
            command.DateOfBirth,
            command.ContactPhone,
            command.ConsentToDataProcessing,
            nowUtc);

        dbContext.Patients.Add(patient);

        // Audit entry references the patient's surrogate id only — no PHI.
        dbContext.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "PatientRegistered",
            EntityType: nameof(Patient),
            EntityId: patient.Id.ToString(),
            PerformedBy: null,
            nowUtc));

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<RegisterPatientResponse>.Success(new RegisterPatientResponse(patient.Id));
    }
}
