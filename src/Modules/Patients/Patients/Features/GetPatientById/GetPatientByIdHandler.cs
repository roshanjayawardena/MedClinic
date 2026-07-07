using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;
using Patients.Persistence;

namespace Patients.Features.GetPatientById;

public sealed class GetPatientByIdHandler(IDbContextFactory<PatientsDbContext> dbFactory)
    : IRequestHandler<GetPatientByIdQuery, Result<GetPatientByIdResponse>>
{
    public async ValueTask<Result<GetPatientByIdResponse>> Handle(
        GetPatientByIdQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var patient = await db.Patients
            .AsNoTracking()
            .Where(p => p.Id == query.PatientId)
            .Select(p => new GetPatientByIdResponse(
                p.Id,
                p.FirstName,
                p.LastName,
                p.DateOfBirth,
                p.ContactPhone,
                p.ConsentToCommunications))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return patient is null
            ? Result<GetPatientByIdResponse>.Fail(new Error("Patient.NotFound", $"Patient {query.PatientId} not found."))
            : Result<GetPatientByIdResponse>.Ok(patient);
    }
}
