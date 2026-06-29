# Database — EF Core, entities, tenant isolation, migrations

Read this before adding an entity, writing a query, or creating a migration.

## Entities

- Domain types live in the module's `Domain/` folder.
- Persistent entities inherit `AuditableEntity` (Id, CreatedAt/By, ModifiedAt/By, soft-delete flag,
  and the tenant key). This gives tenant scoping, soft-delete, and audit fields for free.
- **Behavior over setters:** expose intent methods (`appt.CheckIn()`), keep mutating properties
  `private set`. Invariants and state transitions live in the aggregate, not in handlers.

## Tenant isolation (default-ON)

- The module `DbContext` derives from `BaseDbContext`, which applies a **global query filter** scoping
  every query to the current clinic. **Never remove it; never call `IgnoreQueryFilters()` on clinical data.**
- Opt a type out of tenant scoping only by marking it `IGlobalEntity` (rare — reference/lookup data).
- A subclass `OnModelCreating` must call `base.OnModelCreating(modelBuilder)` **last**, so the base
  filters and conventions win.

```csharp
public sealed class AppointmentsDbContext(DbContextOptions<AppointmentsDbContext> o, ITenantContext t)
    : BaseDbContext(o, t)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(AppointmentsDbContext).Assembly);
        base.OnModelCreating(b);   // LAST
    }
}
```

## Queries

- Project to a DTO with `.Select(...)`; don't return entities from queries.
- Add `.AsNoTracking()` for reads. Pass the `CancellationToken` to every async EF call.
- Paginated reads return a `PagedResponse<T>` (see the query-patterns skill when you build a list).

## Migrations

- Migrations live in `MedClinic.Migrations.PostgreSQL`, organized per-module by folder.
- Use the `create-migration` skill for the exact command — it's footgun-heavy (wrong `--context` or
  `--output-dir` corrupts the snapshot). Always review the generated SQL before applying.
- The DB is migrated by the **DbMigrator**, never at API startup.
