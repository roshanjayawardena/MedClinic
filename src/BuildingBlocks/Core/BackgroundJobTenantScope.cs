namespace Core;

/// <summary>
/// Provides tenant context for code running outside an HTTP request — specifically Hangfire jobs.
/// Set Current before invoking any handler or EF query that reads ITenantContext.
/// AsyncLocal ensures the value is scoped to the current async execution context only.
/// </summary>
public static class BackgroundJobTenantScope
{
    private static readonly AsyncLocal<Guid> _tenantId = new();

    public static Guid Current
    {
        get => _tenantId.Value;
        set => _tenantId.Value = value;
    }

    public static bool IsActive => _tenantId.Value != Guid.Empty;
}
