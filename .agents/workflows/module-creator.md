# module-creator — orchestrate a new bounded context end to end

Use this workflow when adding a completely new module. It sequences `add-module` skill steps,
checks every wiring site, and verifies isolation before a single feature is written.

**Do this once per module, before any features are added.**

---

## Inputs required before starting

```
Module name:        <Name>          (e.g. Appointments)
Order number:       <N>             (used in [assembly: MedClinicModule] attribute ordering)
Key entities:       <Entity1>...    (just names — add-entity handles the implementation)
Cross-module reads: <Dependencies>  (which other modules' Contracts this module will reference)
```

---

## Step 1 — Create the two projects

```bash
# Runtime project
dotnet new classlib -n <Name> -o src/Modules/<Name>/<Name>
# Contracts project
dotnet new classlib -n <Name>.Contracts -o src/Modules/<Name>/<Name>.Contracts
```

Add both to the solution:
```bash
dotnet sln add src/Modules/<Name>/<Name>/<Name>.csproj
dotnet sln add src/Modules/<Name>/<Name>.Contracts/<Name>.Contracts.csproj
```

---

## Step 2 — Set up project references

Runtime references:
```xml
<!-- <Name>/<Name>.csproj -->
<ProjectReference Include="..\<Name>.Contracts\<Name>.Contracts.csproj" />
<ProjectReference Include="..\..\..\BuildingBlocks\Core\MedClinic.BuildingBlocks.Core.csproj" />
<ProjectReference Include="..\..\..\BuildingBlocks\Persistence\MedClinic.BuildingBlocks.Persistence.csproj" />
```

Contracts references (minimal):
```xml
<!-- <Name>.Contracts/<Name>.Contracts.csproj -->
<ProjectReference Include="..\..\..\BuildingBlocks\Core\MedClinic.BuildingBlocks.Core.csproj" />
```

For each cross-module dependency (read-only, Contracts only):
```xml
<ProjectReference Include="..\..\<Dependency>\<Dependency>.Contracts\<Dependency>.Contracts.csproj" />
```

---

## Step 3 — Create the scaffold files

**`<Name>Module.cs`:**
```csharp
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: MedClinicModule(typeof(<Name>Module), order: <N>)]

namespace <Name>;

public sealed class <Name>Module : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<<Name>DbContext>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // features will register here
    }
}
```

**`Persistence/<Name>DbContext.cs`:**
```csharp
namespace <Name>.Persistence;

public sealed class <Name>DbContext(
    DbContextOptions<<Name>DbContext> options,
    ITenantContext tenant)
    : BaseDbContext(options, tenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(<Name>DbContext).Assembly);
        base.OnModelCreating(modelBuilder);   // LAST
    }
}
```

---

## Step 4 — Register in EVERY wiring site (non-negotiable)

Open `src/Host/MedClinic.Api/Program.cs` and add:
```csharp
// In AddMediator(o => { o.Assemblies = new[] { ...
typeof(<Name>Module).Assembly,
typeof(<Name>SomeContract>).Assembly,   // use any type from .Contracts

// In moduleAssemblies array
typeof(<Name>Module).Assembly,
```

Open `src/Host/MedClinic.DbMigrator/Program.cs` and add **the identical entries**.

---

## Step 5 — Verify it loads (smoke test)

```bash
dotnet build
dotnet run --project src/Host/MedClinic.Api
# Hit Scalar UI — confirm the module's (empty) endpoint group appears
```

A build error here is easier to fix than a silent 404 discovered after 10 features are written.

---

## Step 6 — Run architecture-guard

Run the `architecture-guard` workflow and confirm:
- New module is isolated (no runtime cross-references)
- Registered in both Program.cs files
- Feature folder layout is clean (empty `Features/` directory is fine at this point)

---

## Step 7 — Create module rule file

Create `.agents/rules/modules/<name>.md` (lowercase) documenting:
- Module responsibility (in-scope / out-of-scope table)
- Key entities and their lifecycle
- Cross-module dependencies
- Any domain-specific gotchas

Use `.agents/rules/modules/appointments.md` as the template.

---

## Completion checklist

- [ ] Two projects created and added to the solution
- [ ] Project references correct (runtime → Contracts + BuildingBlocks; Contracts → Core only)
- [ ] `<Name>Module.cs` with assembly attribute and order number
- [ ] `<Name>DbContext` inheriting `BaseDbContext`, `base.OnModelCreating` called last
- [ ] Registered in `Api/Program.cs` (Mediator assemblies + moduleAssemblies)
- [ ] Registered in `DbMigrator/Program.cs` (identical)
- [ ] `dotnet build` clean
- [ ] `architecture-guard` passes
- [ ] `.agents/rules/modules/<name>.md` created
