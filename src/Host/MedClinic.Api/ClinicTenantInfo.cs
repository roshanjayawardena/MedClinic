using Finbuckle.MultiTenant.Abstractions;

namespace MedClinic.Api;

/// <summary>
/// Finbuckle tenant info for a clinic. The Identifier is the raw GUID string from the
/// X-Tenant-Id header; no database lookup is performed — the PassthroughTenantStore accepts
/// any syntactically valid GUID so every registered clinic can authenticate.
/// </summary>
public sealed class ClinicTenantInfo : ITenantInfo
{
    public string Id { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? ConnectionString { get; set; }
}
