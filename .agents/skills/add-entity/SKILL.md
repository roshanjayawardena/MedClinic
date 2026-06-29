---
name: add-entity
description: >
  Use when adding a domain aggregate or entity with its EF configuration and a
  migration. Triggers on "add entity", "model the X aggregate", "create the
  Appointment/Encounter/Prescription entity".
---

# Add an entity / aggregate

Read first: `database.md`; for patient/clinical data also `phi-and-tenancy.md`.

1. Create the type in the module's `Domain/` folder, inheriting `AuditableEntity` (tenant key,
   soft-delete, audit fields included).
2. **Model behavior, not just data:** mutating properties are `private set`; expose intent methods that
   enforce invariants and state transitions and return `Result` (e.g. `CheckIn()`, `Cancel(reason)`).
   Illegal states must be impossible to construct.
3. Add an `IEntityTypeConfiguration<T>` in `Persistence/Configurations/` (keys, lengths, indexes,
   relationships). It's picked up by `ApplyConfigurationsFromAssembly`.
4. Expose a `DbSet<T>` on the module DbContext.
5. Create the migration via the `create-migration` skill; review the SQL.
6. **Verify:** `dotnet build`, `dotnet test`.
