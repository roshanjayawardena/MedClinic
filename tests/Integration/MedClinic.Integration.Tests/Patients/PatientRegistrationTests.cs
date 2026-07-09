using FluentAssertions;
using MedClinic.Integration.Tests.Infrastructure;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;
using Patients.Features.RegisterPatient;

namespace MedClinic.Integration.Tests.Patients;

/// <summary>
/// Integration tests for RegisterPatientHandler — verifies DB persistence via a real PostgreSQL container.
/// Each test uses a unique tenantId so tests are fully isolated without any cleanup ceremony.
///
/// Architecture note: validation (consent check) is an IEndpointFilter that runs at the HTTP layer,
/// not inside the handler. The handler trusts that the validator has already executed.
/// Validator logic is covered by RegisterPatientValidatorTests (pure unit test, no DB).
/// </summary>
public sealed class PatientRegistrationTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task RegisterPatient_WithConsent_PersistsPatientToDatabase()
    {
        var tenantId = Guid.NewGuid();
        var dbFactory = fixture.BuildPatientsDbContextFactory(tenantId);
        var tenantContext = new TestTenantContext(tenantId);
        var handler = new RegisterPatientHandler(dbFactory, tenantContext, TimeProvider.System);

        var command = new RegisterPatientCommand(
            FirstName: "Alice",
            LastName: "Nguyen",
            DateOfBirth: new DateOnly(1985, 3, 12),
            ContactPhone: "+64 21 555 0101",
            ConsentToDataProcessing: true,
            ConsentToCommunications: true);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PatientId.Should().NotBeEmpty();

        // Verify the patient is actually in the database.
        await using var verify = fixture.BuildPatientsDbContext(tenantId);
        var saved = await verify.Patients.SingleOrDefaultAsync(p => p.Id == result.Value.PatientId);
        saved.Should().NotBeNull();
        saved!.ConsentToDataProcessing.Should().BeTrue();
        saved.ConsentToCommunications.Should().BeTrue();
        saved.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task RegisterPatient_WithoutCommunicationsConsent_StillSucceeds()
    {
        // ConsentToCommunications defaults to false — this is a valid registration.
        // The consent check that BLOCKS registration is ConsentToDataProcessing,
        // which is enforced by the validator at the endpoint layer, not the handler.
        var tenantId = Guid.NewGuid();
        var dbFactory = fixture.BuildPatientsDbContextFactory(tenantId);
        var tenantContext = new TestTenantContext(tenantId);
        var handler = new RegisterPatientHandler(dbFactory, tenantContext, TimeProvider.System);

        var command = new RegisterPatientCommand(
            FirstName: "Bob",
            LastName: "Smith",
            DateOfBirth: new DateOnly(1970, 6, 1),
            ContactPhone: "+64 21 555 0202",
            ConsentToDataProcessing: true,
            ConsentToCommunications: false);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await using var verify = fixture.BuildPatientsDbContext(tenantId);
        var saved = await verify.Patients.SingleOrDefaultAsync(p => p.Id == result.Value.PatientId);
        saved.Should().NotBeNull();
        saved!.ConsentToCommunications.Should().BeFalse();
    }
}
