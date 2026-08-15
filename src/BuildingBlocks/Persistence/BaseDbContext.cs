using System.Reflection;
using Core;
using Microsoft.EntityFrameworkCore;

namespace Persistence;

/// <summary>
/// Base DbContext for every module. Provides two guarantees automatically:
/// 1. Tenant isolation — global query filter on every AuditableEntity subtype.
/// 2. Audit stamping — TenantId, CreatedAt, ModifiedAt, IsDeleted, DeletedAt
///    are set by SaveChangesAsync; handlers never set them directly.
///
/// Derived DbContexts must call base.OnModelCreating(modelBuilder) LAST so
/// these global filters and conventions are not overridden.
/// </summary>
public abstract class BaseDbContext<TContext>(
    DbContextOptions<TContext> options,
    ITenantContext tenantContext,
    ICurrentUserContext currentUserContext,
    TimeProvider timeProvider)
    : DbContext(options)
    where TContext : DbContext
{
    private static readonly MethodInfo ApplyFiltersMethod =
        typeof(BaseDbContext<TContext>)
            .GetMethod(nameof(ApplyGlobalFilters), BindingFlags.NonPublic | BindingFlags.Instance)!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Auto-apply tenant + soft-delete filter to every AuditableEntity subtype.
        // Module DbContexts add their own configurations first, then call base last.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(AuditableEntity).IsAssignableFrom(entityType.ClrType)
                && !entityType.IsOwned())
            {
                ApplyFiltersMethod
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [modelBuilder]);
            }
        }

        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAuditFields();
        return base.SaveChanges();
    }

    // --- private helpers ---

    private void StampAuditFields()
    {
        var now    = timeProvider.GetUtcNow();
        var userId = currentUserContext.UserId; // Guid.Empty for background jobs / anonymous

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // TenantId and CreatedById come from context — never from user-supplied data.
                    entry.Property(nameof(AuditableEntity.TenantId)).CurrentValue   = tenantContext.TenantId;
                    entry.Property(nameof(AuditableEntity.CreatedAt)).CurrentValue  = now;
                    entry.Property(nameof(AuditableEntity.CreatedById)).CurrentValue = userId;
                    break;

                case EntityState.Modified:
                    entry.Property(nameof(AuditableEntity.ModifiedAt)).CurrentValue   = now;
                    entry.Property(nameof(AuditableEntity.ModifiedById)).CurrentValue = userId;
                    // Never let a modification reset immutable insert-time fields.
                    entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified   = false;
                    entry.Property(nameof(AuditableEntity.CreatedById)).IsModified  = false;
                    entry.Property(nameof(AuditableEntity.TenantId)).IsModified    = false;
                    break;

                case EntityState.Deleted:
                    // Intercept hard-delete: convert to soft-delete.
                    entry.State = EntityState.Modified;
                    entry.Property(nameof(AuditableEntity.IsDeleted)).CurrentValue   = true;
                    entry.Property(nameof(AuditableEntity.DeletedAt)).CurrentValue   = now;
                    entry.Property(nameof(AuditableEntity.ModifiedById)).CurrentValue = userId;
                    break;
            }
        }
    }

    private void ApplyGlobalFilters<TEntity>(ModelBuilder builder) where TEntity : AuditableEntity
    {
        builder.Entity<TEntity>()
            .HasQueryFilter(e => !e.IsDeleted && e.TenantId == tenantContext.TenantId);
    }
}
