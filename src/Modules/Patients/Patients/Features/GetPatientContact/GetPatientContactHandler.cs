using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;
using Patients.Persistence;

namespace Patients.Features.GetPatientContact;

public sealed class GetPatientContactHandler(IDbContextFactory<PatientsDbContext> dbFactory)
    : IRequestHandler<GetPatientContactQuery, Result<GetPatientContactResponse>>
{
    public async ValueTask<Result<GetPatientContactResponse>> Handle(
        GetPatientContactQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var patient = await db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == query.PatientId, cancellationToken)
            .ConfigureAwait(false);

        if (patient is null)
            return Result<GetPatientContactResponse>.Fail(
                new Error("Patient.NotFound", $"Patient {query.PatientId} not found."));

        return Result<GetPatientContactResponse>.Ok(
            new GetPatientContactResponse(
                patient.Id,
                patient.ContactPhone,
                patient.ConsentToCommunications));
    }
}
