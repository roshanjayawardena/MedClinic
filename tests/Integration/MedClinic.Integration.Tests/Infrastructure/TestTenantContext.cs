using Core;

namespace MedClinic.Integration.Tests.Infrastructure;

/// <summary>
/// Test double for ITenantContext. Each test instantiates this with a unique ClinicId
/// so that data written in one test is invisible to another — mirroring the production
/// query-filter behaviour without any test-cleanup ceremony.
/// </summary>
public sealed class TestTenantContext(Guid tenantId) : ITenantContext
{
    public Guid TenantId { get; } = tenantId;

    public static TestTenantContext New() => new(Guid.NewGuid());
}
