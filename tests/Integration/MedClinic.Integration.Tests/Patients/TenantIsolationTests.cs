using FluentAssertions;
using MedClinic.Integration.Tests.Infrastructure;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;
using Patients.Features.RegisterPatient;

namespace MedClinic.Integration.Tests.Patients;

/// <summary>
/// Verifies that the EF global query filter enforces tenant isolation at the database layer.
/// This is the highest-value test in the suite: a missing or incorrect query filter would
/// let clinic B read clinic A's patient records — a catastrophic data breach.
/// </summary>
public sealed class TenantIsolationTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Patient_RegisteredUnderClinicA_IsNotVisibleToClinicB()
    {
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();

        // Register a patient under clinic A.
        var handlerA = new RegisterPatientHandler(
            fixture.BuildPatientsDbContextFactory(clinicA),
            new TestTenantContext(clinicA),
            TimeProvider.System);

        var result = await handlerA.Handle(
            new RegisterPatientCommand(
                FirstName: "Protected",
                LastName: "Patient",
                DateOfBirth: new DateOnly(1990, 1, 1),
                ContactPhone: "+64 21 555 0303",
                ConsentToDataProcessing: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var patientId = result.Value.PatientId;

        // A DbContext scoped to clinic A can find the patient.
        await using var dbA = fixture.BuildPatientsDbContext(clinicA);
        var visibleToA = await dbA.Patients.SingleOrDefaultAsync(p => p.Id == patientId);
        visibleToA.Should().NotBeNull(because: "the owning clinic must be able to read its own patients");

        // A DbContext scoped to clinic B must NOT find the same patient.
        await using var dbB = fixture.BuildPatientsDbContext(clinicB);
        var visibleToB = await dbB.Patients.SingleOrDefaultAsync(p => p.Id == patientId);
        visibleToB.Should().BeNull(
            because: "the global query filter must prevent cross-tenant reads — " +
                     "clinic B must never see clinic A's patients");
    }
}
