---
name: add-module
description: >
  Use when creating a new bounded context — a whole new module like Appointments,
  Encounters, or Prescriptions. Triggers on "add module", "new module", "create
  the X module".
---

# Add a module (bounded context)

Read first: `architecture.md`.

1. Create two projects: `src/Modules/<Name>/<Name>` (runtime) and `<Name>.Contracts`.
2. Reference BuildingBlocks (Core, Persistence) from the runtime project; Contracts references Core only.
3. Add both to the solution: `dotnet sln add (Get-ChildItem -Recurse -Filter *.csproj).FullName`.
4. `<Name>Module.cs` implements `IModule` with `RegisterServices()` and `MapEndpoints()`, plus the
   assembly attribute: `[assembly: MedClinicModule(typeof(<Name>Module), order)]`.
5. `Persistence/<Name>DbContext.cs` derives from `BaseDbContext` (tenant filter ON); `OnModelCreating`
   calls `base` **last**.
6. **Register in EVERY wiring site** (the silent footgun):
   - `MedClinic.Api/Program.cs`: Mediator `o.Assemblies` markers (runtime **and** Contracts) + the
     `moduleAssemblies` array.
   - `MedClinic.DbMigrator/Program.cs`: the **identical** markers + entries.
7. Add an empty `MapEndpoints()` for now; add a migration folder under `MedClinic.Migrations.PostgreSQL`.
8. **Verify it LOADS:** `dotnet build`, run the architecture tests, and confirm a trivial endpoint is
   discovered (not a silent 404). A missing Mediator marker is the #1 cause of "handler not found".
