using Core;

namespace MedClinic.Migrations.PostgreSQL;

/// <summary>
/// Stub ITenantContext used only at migration design-time and by the DbMigrator.
/// The global query filter in BaseDbContext is applied with this dummy tenant id,
/// but migrations run against the full schema (not tenant-filtered rows), so the
/// value here never affects which data is touched.
/// </summary>
public sealed class MigrationTenantContext : ITenantContext
{
    public Guid TenantId => Guid.Empty;
}
