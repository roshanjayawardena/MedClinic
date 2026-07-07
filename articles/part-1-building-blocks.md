# Building Blocks: The Shared Infrastructure Every Module Depends On

## MediClinic Part 1 — How we built the foundation that makes tenant isolation, audit stamping, and structured errors automatic for every module that follows

---

> This is Part 1 of the **MediClinic** series — a reference implementation for AI-native SaaS
> development on .NET 10. We're building a real clinic management system for a single-doctor
> private practice, published as a step-by-step article series. Every architectural decision is
> explained. Every gotcha is documented.
>
> [Part 0 — The Blueprint](part-0-the-blueprint.md) |
> **Part 1 — Building Blocks** |
> Part 2 — The Patients Module *(coming soon)*

---

If you have ever joined a codebase where every module solved the same problem differently — one
used exceptions for business errors, another used a `bool` return, a third silently swallowed
failures — you know what happens when there is no shared foundation. Every new feature is a
guess. Every bug is a surprise.

Building Blocks are the answer. They are the code that every module in MediClinic will depend on.
They make the right thing easy and the wrong thing hard. Tenant isolation is automatic. Audit
stamps are automatic. Soft-delete is automatic. A handler author does not need to think about any
of it.

By the end of this article you will have built:

- `Result<T>` — structured error handling without exceptions
- `ITenantContext` — a tenant contract that is safe to inject anywhere
- `AuditableEntity` — a base class that stamps every clinical record
- `BaseDbContext` — a global query filter that cannot be bypassed
- `ValidationFilter` — FluentValidation running before the handler, not inside it
- A working Mediator setup that survives the multi-project challenge
- A running PostgreSQL database via Docker
- A Scalar API explorer for interactive testing

Let's build.

---

## The Folder Structure

Three small projects. No cross-dependencies.

```
src/BuildingBlocks/
├── Core/          ← interfaces, base classes, Result<T>
├── Persistence/   ← BaseDbContext
└── Web/           ← ValidationFilter
```

`Core` has zero external dependencies. `Persistence` depends on `Core` and EF Core. `Web`
depends on `Core` and FluentValidation. Modules reference whichever of these they need.

---

## Result\<T\> — Because Exceptions Are Not Control Flow

The first rule we established in Part 0: **handlers return `Result<T>`, they never throw for
expected failures**.

This is not a preference. It is enforced by the return type.

When a patient registration fails because the patient already exists, that is not exceptional —
it is a business rule violation. Throwing an exception for it means your caller needs a try/catch
to do something perfectly ordinary. `Result<T>` makes the failure path as first-class as the
success path.

```csharp
// src/BuildingBlocks/Core/Result.cs

public readonly struct Result<T>
{
    private readonly T?     _value;
    private readonly Error? _error;

    private Result(T value)     { _value = value; IsSuccess = true; }
    private Result(Error error) { _error = error; IsSuccess = false; }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public T     Value => IsSuccess ? _value! : throw new InvalidOperationException("Result has no value.");
    public Error Error => IsFailure ? _error! : throw new InvalidOperationException("Result has no error.");

    public static Result<T> Ok(T value)          => new(value);
    public static Result<T> Fail(string message) => new(new Error("Error", message));
    public static Result<T> Fail(Error error)    => new(error);
}

public readonly record struct Error(string Code, string Description);
```

We also add a non-generic `Result` for commands that return nothing (a delete, a status update)
and static helpers so the type parameter is inferred at the call site:

```csharp
public readonly struct Result
{
    public static Result Ok()                  => new(true, null);
    public static Result Fail(string message)  => new(false, new Error("Error", message));

    public static Result<T> Ok<T>(T value)          => Result<T>.Ok(value);
    public static Result<T> Fail<T>(string message) => Result<T>.Fail(message);
}
```

A handler returns success like this:

```csharp
return Result<RegisterPatientResponse>.Ok(new RegisterPatientResponse(patient.Id));
```

The endpoint maps it to HTTP without a try/catch in sight:

```csharp
return result.IsSuccess
    ? TypedResults.Created($"/patients/{result.Value.PatientId}", result.Value)
    : TypedResults.ValidationProblem(new Dictionary<string, string[]>
        { [result.Error.Code] = [result.Error.Message] });
```

---

## ITenantContext — The Tenant Contract

MediClinic is multi-tenant. Clinic A cannot see Clinic B's patients, not by accident and not by
design. The enforcement mechanism is `ITenantContext`:

```csharp
// src/BuildingBlocks/Core/ITenantContext.cs
public interface ITenantContext
{
    Guid TenantId { get; }
}
```

One property. That is the entire contract.

At runtime, the API host resolves the tenant from an `X-Tenant-Id` request header:

```csharp
// src/Host/MedClinic.Api/HttpTenantContext.cs
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid TenantId
    {
        get
        {
            var header = accessor.HttpContext?.Request.Headers["X-Tenant-Id"].ToString();
            if (!Guid.TryParse(header, out var tenantId))
                throw new InvalidOperationException("No tenant could be resolved for the current request.");
            return tenantId;
        }
    }
}
```

At design time — when `dotnet ef migrations add` runs — there is no HTTP context. The migrations
project provides a stub:

```csharp
// src/Host/MedClinic.Migrations.PostgreSQL/MigrationTenantContext.cs
public sealed class MigrationTenantContext : ITenantContext
{
    public Guid TenantId => Guid.Empty;
}
```

### Register as Singleton, not Scoped

If you followed standard ASP.NET Core tutorials you would register `ITenantContext` as
`AddScoped<ITenantContext, HttpTenantContext>()`. In MediClinic we register it as `AddSingleton`.

The reason is Mediator. Source-generated Mediator registers handlers as **Singleton** by default.
A Singleton cannot consume a Scoped dependency — the DI container throws at startup. We will get
to Mediator properly in a moment, but this is the consequence: anything injected into a handler
must be Singleton-safe.

`HttpTenantContext` is safe as a Singleton because `TenantId` is not a cached value. It reads
`accessor.HttpContext?.Request.Headers` on every access. `IHttpContextAccessor` is itself
Singleton, backed by `AsyncLocal<T>` — so `HttpContext` is always the *current* request's context,
regardless of which thread or when you read it. The Singleton wrapper is a thin reader over
request-scoped state that is already thread-isolated by the runtime.

```csharp
// Program.cs
builder.Services.AddSingleton<ITenantContext, HttpTenantContext>();
```

---

## AuditableEntity — Stamps Every Clinical Record

Every table in MediClinic that holds patient or clinical data needs the same set of columns:

- `Id` — a stable Guid set at creation
- `TenantId` — which clinic owns this record
- `CreatedAt`, `ModifiedAt` — UTC timestamps
- `IsDeleted`, `DeletedAt` — for soft-delete and regulatory retention

Rather than repeating this on every entity, we put it in a base class:

```csharp
// src/BuildingBlocks/Core/AuditableEntity.cs
public abstract class AuditableEntity
{
    protected AuditableEntity() { }

    public Guid Id             { get; protected set; }
    public Guid TenantId       { get; private set; }
    public DateTimeOffset  CreatedAt  { get; private set; }
    public DateTimeOffset? ModifiedAt { get; private set; }
    public bool IsDeleted      { get; private set; }
    public DateTimeOffset? DeletedAt  { get; private set; }
}
```

Notice what `private set` does: `TenantId` cannot be set by a subclass or by any handler. Only
`BaseDbContext` sets it, by reaching directly into EF's `ChangeTracker`. This is a structural
guarantee — not a convention that someone can forget to follow.

A domain entity inherits and adds its own fields:

```csharp
// src/Modules/Patients/Patients/Domain/Patient.cs
public sealed class Patient : AuditableEntity
{
    private Patient() { } // EF Core parameterless constructor

    public string   FirstName    { get; private set; } = string.Empty;
    public string   LastName     { get; private set; } = string.Empty;
    public DateOnly DateOfBirth  { get; private set; }
    public string   ContactPhone { get; private set; } = string.Empty;
    public bool ConsentToDataProcessing { get; private set; }
    public bool ConsentToCommunications { get; private set; }

    public static Patient Register(
        string firstName, string lastName, DateOnly dateOfBirth,
        string contactPhone, bool consentToDataProcessing, bool consentToCommunications) =>
        new()
        {
            Id        = Guid.NewGuid(), // ← known before save, returned in response immediately
            FirstName = firstName,
            LastName  = lastName,
            DateOfBirth  = dateOfBirth,
            ContactPhone = contactPhone,
            ConsentToDataProcessing = consentToDataProcessing,
            ConsentToCommunications = consentToCommunications,
            // TenantId and CreatedAt are NOT set here
            // BaseDbContext.SaveChangesAsync stamps them
        };
}
```

`Id` is set in the factory method because the HTTP response returns it immediately — we need it
before the row is saved. `TenantId` is set by the infrastructure, because no domain code should
ever need to know which clinic it is running inside.

---

## BaseDbContext — The Filter That Cannot Be Skipped

`BaseDbContext<TContext>` is the most important piece of infrastructure in the whole project.
It provides three automatic behaviours for every `AuditableEntity` subtype.

### 1. Global query filter

The approach: a private generic helper applied via reflection so we get a clean lambda that EF
Core can translate to SQL.

```csharp
// src/BuildingBlocks/Persistence/BaseDbContext.cs
public abstract class BaseDbContext<TContext>(
    DbContextOptions<TContext> options,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : DbContext(options) where TContext : DbContext
{
    private static readonly MethodInfo ApplyFiltersMethod =
        typeof(BaseDbContext<TContext>)
            .GetMethod(nameof(ApplyGlobalFilters), BindingFlags.NonPublic | BindingFlags.Instance)!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

    private void ApplyGlobalFilters<TEntity>(ModelBuilder builder) where TEntity : AuditableEntity
    {
        builder.Entity<TEntity>()
            .HasQueryFilter(e => !e.IsDeleted && e.TenantId == tenantContext.TenantId);
    }
}
```

The key detail in `ApplyGlobalFilters`: `tenantContext.TenantId` is captured as a **closure
variable**, not as a constant. EF Core evaluates it at query execution time, not at model-build
time. This means the filter always uses the correct clinic for the current request — even though
the model is built once at startup.

Every `SELECT` against a `Patient` table automatically becomes:

```sql
WHERE is_deleted = false AND tenant_id = @currentTenantId
```

A module author cannot forget this filter. They would have to explicitly call
`.IgnoreQueryFilters()` to bypass it, and the architecture tests will catch that.

### 2. Audit stamping

```csharp
public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    StampAuditFields();
    return await base.SaveChangesAsync(cancellationToken);
}

private void StampAuditFields()
{
    var now = timeProvider.GetUtcNow(); // ← testable; never DateTime.UtcNow directly

    foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
    {
        switch (entry.State)
        {
            case EntityState.Added:
                entry.Property(nameof(AuditableEntity.TenantId)).CurrentValue  = tenantContext.TenantId;
                entry.Property(nameof(AuditableEntity.CreatedAt)).CurrentValue = now;
                break;

            case EntityState.Modified:
                entry.Property(nameof(AuditableEntity.ModifiedAt)).CurrentValue = now;
                entry.Property(nameof(AuditableEntity.TenantId)).IsModified  = false;
                entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified  = false;
                break;

            case EntityState.Deleted:
                // Hard-delete intercepted → converted to soft-delete
                entry.State = EntityState.Modified;
                entry.Property(nameof(AuditableEntity.IsDeleted)).CurrentValue  = true;
                entry.Property(nameof(AuditableEntity.DeletedAt)).CurrentValue  = now;
                break;
        }
    }
}
```

`TimeProvider.GetUtcNow()` instead of `DateTime.UtcNow` is not a preference — it is what makes
timestamps testable. Integration tests inject a `FakeTimeProvider` and assert that `CreatedAt` is
exactly what they expect.

### 3. Soft-delete interception

When a handler calls `db.Patients.Remove(patient)`, EF marks the entity as `EntityState.Deleted`.
`StampAuditFields()` catches this and flips the state to `EntityState.Modified`, setting
`IsDeleted = true` and `DeletedAt = now`. The row is never physically removed. It disappears from
all future queries (filtered by `HasQueryFilter`) but is permanently retained for audit and
regulatory purposes.

---

## ValidationFilter — Validation as a Gateway

With source-generated Mediator there is no `IPipelineBehavior<T>` like in MediatR. We use
ASP.NET Core's `IEndpointFilter` instead:

```csharp
// src/BuildingBlocks/Web/ValidationFilter.cs
public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var request = ctx.Arguments.OfType<TRequest>().FirstOrDefault();

        if (request is null)
            return Results.BadRequest("Request body is required.");

        var result = await validator.ValidateAsync(request, ctx.HttpContext.RequestAborted)
                                    .ConfigureAwait(false);

        if (!result.IsValid)
            return Results.ValidationProblem(result.ToDictionary());

        return await next(ctx).ConfigureAwait(false);
    }
}
```

An invalid request never reaches the handler. The endpoint wires it up with one line:

```csharp
app.MapPost("/patients", Handle)
    .AddEndpointFilter<ValidationFilter<RegisterPatientCommand>>();
```

---

## Source-Generated Mediator — The Multi-Project Challenge

This is the section most tutorials skip, and it is where real projects get stuck.

We use **martinothamar/Mediator v2.1.7** — a Roslyn source generator that produces a zero-overhead
dispatcher at compile time. It is fundamentally different from MediatR:

- No `IRequest<T>` from MediatR
- No pipeline behaviours
- No assembly scanning at runtime
- Handlers are **Singleton** by default

### Where each package goes

There are two packages and they must go in different places:

| Package | What it provides | Where it lives |
|---|---|---|
| `Mediator.Abstractions` | Interfaces: `IRequest<T>`, `IRequestHandler<T,R>`, `IMediator` | Contracts project, module project |
| `Mediator.SourceGenerator` | Generates: `Mediator` class, `AddMediator()` extension | Module project only |

```xml
<!-- Patients.Contracts.csproj — the command lives here -->
<PackageReference Include="Mediator.Abstractions" Version="2.1.7" />

<!-- Patients.csproj — the handler lives here -->
<PackageReference Include="Mediator.SourceGenerator" Version="2.1.7">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

The source generator runs inside the Patients compilation. It finds `RegisterPatientHandler`,
generates a concrete `Mediator` dispatcher, and generates `AddMediator()` on `IServiceCollection`.
This code is compiled into `Patients.dll` and flows to the API host via the project reference.

**`PrivateAssets="all"` is not optional.** Without it the source generator flows transitively
into the API host compilation, runs a second time, generates a second `AddMediator()`, and you
get an ambiguous call error at compile time. This is exactly what happened to us.

In `Program.cs`:

```csharp
builder.Services.AddMediator(); // from Patients.dll — no package reference needed in API project
```

### Handlers are Singleton — the IDbContextFactory solution

Source-generated Mediator registers handlers as **Singleton**. `DbContext` is **Scoped** (one
per HTTP request). Inject a Scoped service into a Singleton and the DI container throws at startup:

```
Cannot consume scoped service 'DbContextOptions<PatientsDbContext>'
from singleton 'RegisterPatientHandler'.
```

The solution is `IDbContextFactory<T>`. Register the factory instead of the context:

```csharp
// PatientsModule.cs — RegisterServices
services.AddDbContextFactory<PatientsDbContext>((sp, options) =>
    options.UseNpgsql(
        configuration["ConnectionStrings:DefaultConnection"],
        npg => npg
            .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
            .MigrationsHistoryTable("__EFMigrationsHistory", "patients")));
```

And in the handler, create a context inside `Handle()`:

```csharp
public sealed class RegisterPatientHandler(
    IDbContextFactory<PatientsDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<RegisterPatientCommand, Result<RegisterPatientResponse>>
{
    public async ValueTask<Result<RegisterPatientResponse>> Handle(
        RegisterPatientCommand command, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken)
                                            .ConfigureAwait(false);

        var patient = Patient.Register(
            command.FirstName, command.LastName, command.DateOfBirth,
            command.ContactPhone, command.ConsentToDataProcessing,
            command.ConsentToCommunications);

        db.Patients.Add(patient);

        db.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(), tenantContext.TenantId,
            Action: "PatientRegistered", EntityType: nameof(Patient),
            EntityId: patient.Id.ToString(), PerformedBy: null,
            timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<RegisterPatientResponse>.Ok(new RegisterPatientResponse(patient.Id));
    }
}
```

`await using` disposes the context after each request. `IDbContextFactory` is Singleton-safe.
Each `Handle()` call gets a fresh, isolated context — exactly the right lifetime.

---

## Centralized Migrations — and Three Non-Obvious Gotchas

We keep every module's migrations in one project and apply them at deploy time with a dedicated
console app. The API host never runs migrations at startup.

```
src/Host/
├── MedClinic.Migrations.PostgreSQL/
│   ├── DesignTime/
│   │   └── PatientsDesignTimeFactory.cs
│   └── Migrations/
│       └── Patients/
│           ├── 20260629_InitialPatientsCreate.cs
│           ├── 20260629_InitialPatientsCreate.Designer.cs  ← do not delete this
│           └── PatientsDbContextModelSnapshot.cs
└── MedClinic.DbMigrator/
    ├── Program.cs
    └── appsettings.json
```

### Gotcha 1 — The Designer.cs file is required

When migrations and the `DbContext` live in the same assembly, EF finds migrations automatically.
When they live in **separate** assemblies (our case), EF requires `[DbContext]` and `[Migration]`
attributes on each migration class:

```csharp
[DbContext(typeof(PatientsDbContext))]
[Migration("20260629133116_InitialPatientsCreate")]
partial class InitialPatientsCreate { ... }
```

`dotnet ef migrations add` auto-generates a `.Designer.cs` file alongside every migration that
carries these attributes. **If you delete it, EF finds zero migrations and silently does nothing.**
We learned this the hard way — the migrator reported "All migrations applied successfully" while
applying exactly nothing.

### Gotcha 2 — Force-load the migrations assembly

The DbMigrator references `MedClinic.Migrations.PostgreSQL` but no code in it directly uses any
type from that assembly. Without a direct type reference, .NET does not load the assembly at
runtime. EF's `MigrationsAssembly` search finds nothing.

The fix is one line:

```csharp
Assembly.Load("MedClinic.Migrations.PostgreSQL");
```

This forces the assembly into the AppDomain before EF starts scanning.

### Gotcha 3 — Content root when running from the solution root

`Host.CreateApplicationBuilder` uses `Directory.GetCurrentDirectory()` as the content root.
When you run `dotnet run --project src/Host/MedClinic.DbMigrator` from the solution root, the
current directory is the solution root — not the project folder. The `appsettings.json` is
copied to the build output (`AppContext.BaseDirectory`), so we point there explicitly:

```csharp
var host = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
```

The complete DbMigrator startup:

```csharp
Assembly.Load("MedClinic.Migrations.PostgreSQL");

var host = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var connStr = host.Configuration["ConnectionStrings:DefaultConnection"]
    ?? throw new InvalidOperationException("Connection string is missing.");

host.Services.AddDbContext<PatientsDbContext>(o =>
    o.UseNpgsql(connStr, npg => npg
        .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
        .MigrationsHistoryTable("__EFMigrationsHistory", "patients"))
     .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

host.Services.AddSingleton<ITenantContext>(new MigrationTenantContext());
host.Services.AddSingleton(TimeProvider.System);

var app = host.Build();
await using var scope = app.Services.CreateAsyncScope();

Console.WriteLine("Applying MedClinic migrations...");
await scope.ServiceProvider.GetRequiredService<PatientsDbContext>().Database.MigrateAsync();
Console.WriteLine("  ✓ Patients");
Console.WriteLine("All migrations applied successfully.");
```

The `PendingModelChangesWarning` suppression is there because EF compares the live model (which
includes our closure-based query filter) against the serialized snapshot. The comparison can
produce a false positive when service-injected query filters are involved. The migration SQL is
correct; the warning is noise.

---

## Local Database — Docker in One Command

No installation required. One command creates a PostgreSQL 16 container and keeps it running
across restarts:

```bash
docker run -d \
  --name mediclinic_postgres \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=mediclinic_dev \
  -p 5433:5432 \
  --restart unless-stopped \
  postgres:16
```

We use **port 5433** to avoid conflicting with any other PostgreSQL container already running on
the default 5432.

`appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5433;Database=mediclinic_dev;Username=postgres;Password=postgres;Include Error Detail=true"
  }
}
```

Apply migrations:

```bash
dotnet run --project src/Host/MedClinic.DbMigrator
```

After a successful run:

```
Applying MedClinic migrations...
  info: Applying migration '20260629133116_InitialPatientsCreate'.
  ✓ Patients
All migrations applied successfully.
```

PostgreSQL now has a `patients` schema containing `patients`, `audit_entries`, and
`__EFMigrationsHistory`.

---

## API Explorer with Scalar

We use **Scalar** instead of Swagger UI — it is faster, has a significantly cleaner UI, and
works out of the box with ASP.NET Core's built-in OpenAPI support.

```csharp
// Program.cs
builder.Services.AddOpenApi();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "MediClinic API";
        options.Theme = ScalarTheme.Purple;
    });
}
```

Run the API and open `https://localhost:7247/scalar/v1`.

Every Patients endpoint requires an `X-Tenant-Id` header (a GUID identifying the clinic). In
Scalar, add it once under **Headers** in the left panel and it is sent automatically with every
request during development.

---

## The Complete Program.cs

After all the pieces are in place, the host entry point is clean and readable:

```csharp
// src/Host/MedClinic.Api/Program.cs
using Core;
using MedClinic.Api;
using Patients;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITenantContext, HttpTenantContext>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOpenApi();

var patientsModule = new PatientsModule();
patientsModule.RegisterServices(builder.Services, builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => { o.Title = "MediClinic API"; o.Theme = ScalarTheme.Purple; });
}

app.UseHttpsRedirection();
patientsModule.MapEndpoints(app);

app.Run();
```

Seven lines of configuration. No ceremony. Every module registers itself; the host just calls
through.

---

## What We Have After Part 1

```
✓ Result<T>           — business errors as data, no exceptions
✓ ITenantContext      — Singleton-safe, lazy tenant resolution per request
✓ AuditableEntity     — automatic Id, TenantId, CreatedAt, ModifiedAt, soft-delete
✓ BaseDbContext       — closure-based global filter, audit stamping, soft-delete interception
✓ ValidationFilter    — FluentValidation as an endpoint filter, not inside handlers
✓ Mediator v2.1.7     — source generator in the module, IDbContextFactory in the handler
✓ Centralized migrations — one project, Designer.cs files intact, assembly force-loaded
✓ DbMigrator          — deploy-time runner with three non-obvious fixes documented
✓ Docker PostgreSQL   — mediclinic_postgres on port 5433, schema created
✓ Scalar API explorer — https://localhost:7247/scalar/v1
✓ Clean build: 0 errors, 0 warnings (excluding known vulnerability in transitive packages)
```

---

## Lessons — What Actually Bit Us

Writing from a working codebase means we can document what went wrong, not just what the plan was.

**Mediator.SourceGenerator in the wrong project.** First instinct: add the source generator to the
API host so it can see all modules. Result: the generator runs in the API compilation, finds no
handlers (handlers are in `Patients.dll`, not in the API's source), generates nothing, `IMediator`
cannot be resolved at runtime. Fix: the generator must run where the handlers are defined, with
`PrivateAssets="all"` to prevent a duplicate `AddMediator()` in the host.

**Singleton handler, Scoped DbContext.** Mediator registers handlers as Singleton. ASP.NET Core
validates service lifetimes at startup and throws. The fix — `IDbContextFactory<T>` — creates a
context inside `Handle()` and disposes it with `await using`. The factory is Singleton-safe. Each
call gets an isolated context.

**ITenantContext lifetime.** Same problem — Scoped service in a Singleton handler. The solution is
registering `HttpTenantContext` as Singleton, which is safe because `TenantId` reads lazily from
`IHttpContextAccessor` on every access. `IHttpContextAccessor` tracks the current request via
`AsyncLocal<T>`, so the value is always correct even from a Singleton.

**The missing Designer.cs.** EF Core requires `[DbContext]` and `[Migration]` attributes on each
migration class when the migrations assembly differs from the DbContext assembly. These live in the
auto-generated `.Designer.cs` file. Without it, EF finds zero migrations, applies zero migrations,
and reports success. Silence is the worst kind of failure.

**DbMigrator content root.** `Host.CreateApplicationBuilder` uses `Directory.GetCurrentDirectory()`.
Run from the solution root and that is the solution root — `appsettings.json` is not there. Point
to `AppContext.BaseDirectory` (the build output) with `HostApplicationBuilderSettings`.

**`Assembly.Load` to force discovery.** The migrations DLL is referenced but never directly used in
the DbMigrator source. .NET does not load it into the AppDomain. EF cannot find the migrations. One
`Assembly.Load("MedClinic.Migrations.PostgreSQL")` call fixes it.

---

## What's Next

In Part 2 we complete the Patients module end-to-end:

- `GetPatientQuery` and `ListPatientsQuery` — read side with pagination
- `UpdatePatientCommand` — demonstrating `ModifiedAt` stamping
- Testcontainers integration tests — real PostgreSQL, real migrations, tenant isolation proved with actual queries

The building blocks we built today make all of that work without module authors needing to think
about tenancy, auditing, or error handling.

---

*The source code for this article is tagged `article/part-1` in the repository.*

*[Part 0 — The Blueprint](part-0-the-blueprint.md) | **Part 1 — Building Blocks** | Part 2 — coming soon*
