namespace Core;

/// <summary>
/// Abstraction over the current tenant (clinic), backed by Finbuckle at the host level.
/// Module DbContexts depend on this to apply tenant-scoping query filters.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
}
