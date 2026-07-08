using Core;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;
using Patients.Domain;
using Patients.Persistence;

namespace Patients.Features.AddAllergy;

public sealed class AddAllergyHandler(IDbContextFactory<PatientsDbContext> dbFactory)
    : IRequestHandler<AddAllergyCommand, Result<AddAllergyResponse>>
{
    public async ValueTask<Result<AddAllergyResponse>> Handle(
        AddAllergyCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var patientExists = await db.Patients
            .AnyAsync(p => p.Id == command.PatientId, cancellationToken)
            .ConfigureAwait(false);

        if (!patientExists)
            return Result<AddAllergyResponse>.Fail(
                new Error("Patient.NotFound", $"Patient {command.PatientId} not found."));

        var allergy = Allergy.Record(command.PatientId, command.DrugName, command.Severity, command.Notes);
        db.Allergies.Add(allergy);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<AddAllergyResponse>.Ok(new AddAllergyResponse(allergy.Id));
    }
}
