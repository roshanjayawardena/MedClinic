---
name: create-migration
description: >
  Use when an entity or configuration changed and an EF Core migration is needed.
  Triggers on "create migration", "add migration", "update the database schema".
---

# Create an EF Core migration (footgun-aware)

Read first: `database.md`.

Migrations live in `src/Host/MedClinic.Migrations.PostgreSQL/Migrations/<Module>/`.
Each module has its own `IDesignTimeDbContextFactory` in `DesignTime/` of that same project.
The `MedClinic.DbMigrator` console app applies them at deploy time — never at API startup.

## Step 1 — Build first

```bash
dotnet build
```

The model must be current before EF generates the migration. Stale code → corrupted snapshot.

## Step 2 — Add the migration

Run from the **solution root**. Use the exact `--project`, `--startup-project`, `--context`, and
`--output-dir` — wrong values corrupt the EF snapshot.

```bash
dotnet ef migrations add <MigrationName> \
  --project src/Host/MedClinic.Migrations.PostgreSQL \
  --startup-project src/Host/MedClinic.Migrations.PostgreSQL \
  --context <Module>DbContext \
  --output-dir Migrations/<Module>
```

Examples:
```bash
# Patients — first migration
dotnet ef migrations add InitialPatientsCreate \
  --project src/Host/MedClinic.Migrations.PostgreSQL \
  --startup-project src/Host/MedClinic.Migrations.PostgreSQL \
  --context PatientsDbContext \
  --output-dir Migrations/Patients

# Appointments — adding a new entity
dotnet ef migrations add AddAppointmentTable \
  --project src/Host/MedClinic.Migrations.PostgreSQL \
  --startup-project src/Host/MedClinic.Migrations.PostgreSQL \
  --context AppointmentsDbContext \
  --output-dir Migrations/Appointments
```

## Step 3 — Review the generated migration file

Before applying, open the generated `.cs` file and confirm:
- [ ] Only the expected tables/columns are created or altered
- [ ] No unexpected `DROP` statements
- [ ] The module's schema is correct (e.g., `schema: "patients"`)
- [ ] `TenantId` column is present on every tenant-scoped table
- [ ] `__EFMigrationsHistory` table uses the correct schema (matches `MigrationsHistoryTable` in the factory)

## Step 4 — Apply via DbMigrator (never API startup)

```bash
dotnet run --project src/Host/MedClinic.DbMigrator
```

The migrator applies all pending migrations for all registered DbContexts in dependency order.

## Step 5 — Undo if needed

If the generated migration is wrong, remove it **before** applying:

```bash
dotnet ef migrations remove \
  --project src/Host/MedClinic.Migrations.PostgreSQL \
  --startup-project src/Host/MedClinic.Migrations.PostgreSQL \
  --context <Module>DbContext
```

**Never hand-edit a migration that has already been applied to a database.**

## Adding a new module's DbContext to the migration infrastructure

When a new module is created (via `add-module` skill), also:

1. Add a `DesignTime/<Module>DesignTimeFactory.cs` in `MedClinic.Migrations.PostgreSQL`
   (copy `PatientsDesignTimeFactory.cs`, update context type and schema name).
2. Add `<ProjectReference>` for the new module's runtime project in
   `MedClinic.Migrations.PostgreSQL.csproj`.
3. Add `services.AddDbContext<...>()` and the `await MigrateAsync()` call in
   `MedClinic.DbMigrator/Program.cs`.
4. Register the new module's `MigrationsAssembly` and `MigrationsHistoryTable` in the module's
   own `RegisterServices()` call (see Patients module as reference).
