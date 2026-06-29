---
name: create-migration
description: >
  Use when an entity or configuration changed and an EF Core migration is needed.
  Triggers on "create migration", "add migration", "update the database schema".
---

# Create an EF Core migration (footgun-aware)

Read first: `database.md`.

1. **Build first** so the model is current and the snapshot isn't generated from stale code:
   `dotnet build`.
2. Add the migration with the EXACT projects and context (wrong values corrupt the snapshot):
   ```bash
   dotnet ef migrations add <Name> \
     --project src/Host/MedClinic.Migrations.PostgreSQL \
     --startup-project src/Host/MedClinic.DbMigrator \
     --context <Module>DbContext \
     --output-dir Migrations/<Module>
   ```
3. **Review the generated SQL / migration file** before applying — confirm it does only what you expect
   (no unexpected drops, tenant column present, indexes correct).
4. Apply via the DbMigrator, never at API startup:
   `dotnet run --project src/Host/MedClinic.DbMigrator -- apply`
5. If something looks wrong: `dotnet ef migrations remove` (same --project/--context) and redo — do not
   hand-edit an applied migration.
