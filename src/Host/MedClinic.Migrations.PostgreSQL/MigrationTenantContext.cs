using Core;

namespace MedClinic.Migrations.PostgreSQL;

/// <summary>
/// Stub ITenantContext used only at migration design-time and by the DbMigrator.
/// </summary>
public sealed class MigrationTenantContext : ITenantContext
{
    public Guid TenantId => Guid.Empty;
}

/// <summary>
/// Stub ICurrentUserContext used only at migration design-time and by the DbMigrator.
/// No HTTP context exists during migrations — UserId is Guid.Empty (system actor).
/// </summary>
public sealed class MigrationUserContext : ICurrentUserContext
{
    public Guid UserId        => Guid.Empty;
    public bool IsAuthenticated => false;
}
