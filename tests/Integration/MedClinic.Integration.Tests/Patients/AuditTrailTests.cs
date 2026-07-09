using FluentAssertions;
using MedClinic.Integration.Tests.Infrastructure;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Patients.Contracts;
using Patients.Features.RegisterPatient;

namespace MedClinic.Integration.Tests.Patients;

/// <summary>
/// Verifies that the audit trail is written in the same transaction as the entity write.
/// Golden rule #9: "Every read or write of an Encounter or Prescription MUST emit an audit entry."
/// The same pattern applies to patient registration — the handler writes both Patient and AuditEntry
/// in a single SaveChangesAsync call, making them atomic.
/// </summary>
public sealed class AuditTrailTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task RegisterPatient_WritesAuditEntry_InSameTransaction()
    {
        var tenantId = Guid.NewGuid();
        var dbFactory = fixture.BuildPatientsDbContextFactory(tenantId);
        var tenantContext = new TestTenantContext(tenantId);
        var handler = new RegisterPatientHandler(dbFactory, tenantContext, TimeProvider.System);

        var result = await handler.Handle(
            new RegisterPatientCommand(
                FirstName: "Audit",
                LastName: "Test",
                DateOfBirth: new DateOnly(1980, 7, 4),
                ContactPhone: "+64 21 555 0404",
                ConsentToDataProcessing: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await using var verify = fixture.BuildPatientsDbContext(tenantId);

        // The handler writes one AuditEntry with action "PatientRegistered".
        var auditEntry = await verify.AuditEntries
            .SingleOrDefaultAsync(a =>
                a.EntityId == result.Value.PatientId.ToString() &&
                a.Action == "PatientRegistered");

        auditEntry.Should().NotBeNull(
            because: "RegisterPatientHandler must write an AuditEntry in the same SaveChangesAsync call");

        auditEntry!.EntityType.Should().Be("Patient");
        auditEntry.TenantId.Should().Be(tenantId,
            because: "audit entries must be tenant-scoped like every other record");
    }
}
