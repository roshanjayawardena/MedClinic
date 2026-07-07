using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;
using Patients.Domain;
using Patients.Persistence;

namespace Patients.Features.RegisterPatient;

public sealed class RegisterPatientHandler(
    IDbContextFactory<PatientsDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<RegisterPatientCommand, Result<RegisterPatientResponse>>
{
    public async ValueTask<Result<RegisterPatientResponse>> Handle(
        RegisterPatientCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var patient = Patient.Register(
            command.FirstName,
            command.LastName,
            command.DateOfBirth,
            command.ContactPhone,
            command.ConsentToDataProcessing,
            command.ConsentToCommunications);

        db.Patients.Add(patient);

        db.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "PatientRegistered",
            EntityType: nameof(Patient),
            EntityId: patient.Id.ToString(),
            PerformedBy: null,
            timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<RegisterPatientResponse>.Ok(new RegisterPatientResponse(patient.Id));
    }
}
