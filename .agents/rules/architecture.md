# Architecture — module layout, boundaries, registration

Read this before creating a module, adding a feature, or moving files. It defines **where code lives**.

## Module boundaries (golden rule)

A module references another module **only through its `.Contracts` project**, never the runtime project.
`Modules.Appointments` may reference `Modules.Patients.Contracts`, never `Modules.Patients`.
Cross-module questions go through a Contracts query or service interface (resolved via DI / Mediator).

## Canonical module layout — feature folders, NOT file-type folders

A module is organized by **vertical slice**: everything for one feature lives in one folder. Do **not**
create flat `Handlers/`, `Validators/`, `Endpoints/` folders.

```
src/Modules/<Name>/
├── <Name>/                         # runtime project
│   ├── <Name>Module.cs             # IModule: MapEndpoints() + RegisterServices()
│   ├── Domain/                     # aggregates & entities (behavior, private setters)
│   │   └── <Entity>.cs
│   ├── Persistence/
│   │   ├── <Name>DbContext.cs      # : BaseDbContext (tenant filter ON)
│   │   └── Configurations/         # IEntityTypeConfiguration<T>
│   └── Features/                   # one folder per slice
│       └── <FeatureName>/
│           ├── <FeatureName>Handler.cs     # public sealed, ValueTask<Result<T>>
│           ├── <FeatureName>Validator.cs   # FluentValidation
│           └── <FeatureName>Endpoint.cs    # thin Minimal API, mapped by the module
└── <Name>.Contracts/               # the module's ONLY public surface
    └── <FeatureName>.cs            # Command/Query + Response records (no EF types)
```

> If you find yourself putting `Patient.cs` and `RegisterPatientHandler.cs` in the same flat folder,
> stop — Domain types go in `Domain/`, each feature gets its own folder under `Features/`.

## Building blocks (shared, protected)

`src/BuildingBlocks/` holds Core (Result, ITenantContext, AuditEntry…), Persistence (BaseDbContext,
interceptors, pagination, ModelBuilder extensions), Web (host wiring), and later Eventing/Caching/Storage.
**Do not modify BuildingBlocks without explicit approval** — every module depends on it.

## IModule

Each runtime module implements `IModule` and carries an assembly attribute so it's discovered:

```csharp
[assembly: MedClinicModule(typeof(AppointmentsModule), order: 30)]

public sealed class AppointmentsModule : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration config) { /* DbContext, DI */ }
    public void MapEndpoints(IEndpointRouteBuilder app) { /* call each feature's MapXxx */ }
}
```

## Registration — touches EVERY wiring site (the silent footgun)

The source-generated **Mediator** discovers handlers by assembly. Miss one site and handlers are silently
undiscovered — no error, endpoints just 404. When adding a module, register it in **all** of these:

1. `MedClinic.Api/Program.cs` — the Mediator `o.Assemblies` markers (the module runtime **and** its Contracts) **and** the `moduleAssemblies` array.
2. `MedClinic.DbMigrator/Program.cs` — the **identical** Mediator markers + module-assemblies entries.

Also: add both projects to the solution (`dotnet sln add`), and reference BuildingBlocks from the module.

## Verify

After creating a module: `dotnet build`, then run the architecture tests, then hit one of its endpoints
in Scalar to confirm the handler was actually discovered.
