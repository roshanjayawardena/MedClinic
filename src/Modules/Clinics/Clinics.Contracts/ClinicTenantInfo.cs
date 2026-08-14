using Finbuckle.MultiTenant.Abstractions;

namespace Clinics.Contracts;

/// <summary>
/// Finbuckle tenant info populated from the clinics database on every request.
/// Carries ContactEmail and TimeZoneId so background jobs don't need a second DB hit.
/// </summary>
public sealed class ClinicTenantInfo : ITenantInfo
{
    public string Id { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? ConnectionString { get; set; }

    /// <summary>Doctor's email for the daily digest. Never log this value.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>IANA timezone id, e.g. "Asia/Colombo".</summary>
    public string TimeZoneId { get; set; } = "UTC";
}
