# Your First Vertical Slice: Registering a Patient with Full PHI Safety

*Part 2 of Building a Production Medical Clinic SaaS with AI*

---

In Part 1 we built the shared infrastructure that every module depends on: `Result<T>`, `ITenantContext`, `AuditableEntity`, and `BaseDbContext`. None of it did anything visible — no endpoint, no database record, nothing you could call from a client.

Part 2 changes that. We build the **Patients module** — the reference implementation that every future module will follow. By the end you will have two working endpoints, a domain aggregate that enforces consent at the type level, and automatic tenant isolation that requires zero effort from any handler.

Everything described here lives at git tag `article/part-2`. Checkout that tag and you have the exact code.

---

## Why start with Patients?

Because patients are the centre of gravity for every other module. Appointments belong to a patient. Encounters are about a patient. Prescriptions are written for a patient. If we get the Patient domain model right — and more importantly, if we get the **PHI safety** right — the rest of the modules have a pattern to copy.

Patients are also the module where the stakes are highest for mistakes. A bug that leaks one clinic's patient list to another clinic is a HIPAA incident. We want to show that we cannot even write that bug: the tenant filter is structural, not something a developer has to remember to add.

---

## The two-project structure

Every module in this system is two projects:

```
src/Modules/Patients/
├── Patients/                  ← runtime project
│   ├── Domain/
│   ├── Features/
│   ├── Persistence/
│   └── PatientsModule.cs
└── Patients.Contracts/        ← public API surface
    ├── RegisterPatient.cs
    └── GetPatientById.cs
```

**Why two projects?**

The `Patients.Contracts` project contains only the shapes that other modules are allowed to see: the command and query records, and the response records. Nothing else.

The `Patients` runtime project contains the handler, the validator, the entity, and the DbContext. Other modules have no project reference to this — they cannot call its internals. The only channel out is through Contracts.

This is not an abstract boundary. It is a compile-time boundary. If the Appointments module tries to `new Patient(...)` directly, the build fails. That is the point.

The `add-module` skill in `.agents/skills/add-module/SKILL.md` captures this as a repeatable recipe. If you are following along and adding a module yourself, that skill tells you every wiring site you need to touch — including the silent footgun of Mediator marker registration.

---

## The Patient aggregate

```csharp
// src/Modules/Patients/Patients/Domain/Patient.cs

public sealed class Patient : AuditableEntity
{
    private Patient() { } // required by EF Core

    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public DateOnly DateOfBirth { get; private set; }
    public string ContactPhone { get; private set; } = string.Empty;
    public bool ConsentToDataProcessing { get; private set; }
    public bool ConsentToCommunications { get; private set; }

    public static Patient Register(
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        string contactPhone,
        bool consentToDataProcessing,
        bool consentToCommunications) =>
        new()
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth,
            ContactPhone = contactPhone,
            ConsentToDataProcessing = consentToDataProcessing,
            ConsentToCommunications = consentToCommunications,
        };
}
```

Three things worth calling out.

**Private setters.** All properties are `private set`. After construction you cannot reach in from outside and change `FirstName`. If you need to update contact info in the future, you add an `UpdateContactInfo(...)` method to this class. The mutation is always expressed through a named behaviour on the aggregate, not a property assignment in a handler.

**Factory method, private constructor.** The EF Core parameterless constructor is `private`. The only public way to create a `Patient` is through `Register(...)`. This means every valid `Patient` instance went through that path — you cannot accidentally construct a patient in an invalid state.

**No TenantId, no CreatedAt.** Notice that `Register()` sets `Id` but does not touch `TenantId`, `CreatedAt`, or `IsDeleted`. Those come from `AuditableEntity` and are stamped by `BaseDbContext.SaveChangesAsync`. The handler never needs to think about them. They are stamped from the ambient `ITenantContext`, which Finbuckle resolves from the request's `X-Tenant-Id` header.

---

## The RegisterPatient feature

A vertical slice is one folder with four files:

```
Features/RegisterPatient/
├── RegisterPatientHandler.cs
├── RegisterPatientValidator.cs
└── RegisterPatientEndpoint.cs
```

Plus the contract in the Contracts project:

```
Patients.Contracts/RegisterPatient.cs
```

### The contract

```csharp
// Patients.Contracts/RegisterPatient.cs

public sealed record RegisterPatientCommand(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string ContactPhone,
    bool ConsentToDataProcessing,
    bool ConsentToCommunications = false) : IRequest<Result<RegisterPatientResponse>>;

public sealed record RegisterPatientResponse(Guid PatientId);
```

The contract is a C# `record` — immutable, structural equality, concise. It lives in the Contracts project so the endpoint project can reference it without knowing anything about the handler or the database.

`IRequest<Result<RegisterPatientResponse>>` is the Mediator source-generator interface. Unlike MediatR, there is no reflection at startup. The source generator reads this during compilation and emits the dispatch table as C# code. If you have never used the source-generated Mediator before, this is the key difference: the `IMediator` that gets injected at runtime is a generated class, not a reflection-driven one.

### The validator

```csharp
// Features/RegisterPatient/RegisterPatientValidator.cs

public sealed class RegisterPatientValidator : AbstractValidator<RegisterPatientCommand>
{
    public RegisterPatientValidator(TimeProvider timeProvider)
    {
        RuleFor(c => c.FirstName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.LastName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ContactPhone).NotEmpty().MaximumLength(50);

        RuleFor(c => c.DateOfBirth)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            .WithMessage("Date of birth cannot be in the future.");

        RuleFor(c => c.ConsentToDataProcessing)
            .Equal(true)
            .WithMessage("Consent to data processing is required to register a patient.");
    }
}
```

Two things to notice.

First, `TimeProvider` is injected. We never call `DateTime.UtcNow` directly — golden rule 4. This makes the validator fully testable: you can pass in a `FakeTimeProvider` and control what "now" means.

Second, `ConsentToDataProcessing` must be `true`. This is a **legal requirement**. You cannot register a patient at this clinic without consent. The validator enforces it so the handler never has to check — by the time the handler runs, consent has already been confirmed.

The validator runs *before* the handler. The `ValidationFilter<T>` in `Web/ValidationFilter.cs` (from Part 1) intercepts the request at the Minimal API layer, runs FluentValidation, and returns a 422 if anything fails. The handler only executes on a valid request.

### The handler

```csharp
// Features/RegisterPatient/RegisterPatientHandler.cs

public sealed class RegisterPatientHandler(
    IDbContextFactory<PatientsDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<RegisterPatientCommand, Result<RegisterPatientResponse>>
{
    public async ValueTask<Result<RegisterPatientResponse>> Handle(
        RegisterPatientCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var patient = Patient.Register(
            command.FirstName,
            command.LastName,
            command.DateOfBirth,
            command.ContactPhone,
            command.ConsentToDataProcessing,
            command.ConsentToCommunications);

        db.Patients.Add(patient);

        db.AuditEntries.Add(new AuditEntry(
            Guid.NewGuid(),
            tenantContext.TenantId,
            Action: "PatientRegistered",
            EntityType: nameof(Patient),
            EntityId: patient.Id.ToString(),
            PerformedBy: null,
            timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<RegisterPatientResponse>.Ok(new RegisterPatientResponse(patient.Id));
    }
}
```

**`IDbContextFactory<PatientsDbContext>`** — not `PatientsDbContext` directly. Mediator handlers are registered as singletons by default (it is what makes the source-generated dispatch efficient). DbContext is scoped. You cannot inject a scoped service into a singleton without a lifetime mismatch error. The factory is the correct solution: call `CreateDbContextAsync`, get a fresh context per handler invocation, dispose it when done.

**`AuditEntry`** — every patient registration writes an audit record in the same transaction as the patient record. This is enforced by convention, not by a framework. The `phi-and-tenancy.md` rule says every patient write must produce an audit trace. The handler obeys it.

**`Result<T>` instead of throw** — golden rule 1. The handler returns `Result<RegisterPatientResponse>.Ok(...)` on success. If a business rule fails, it returns `Result.Fail(...)`. Nothing throws for an expected failure. The endpoint maps the result to the appropriate HTTP status.

### The endpoint

```csharp
// Features/RegisterPatient/RegisterPatientEndpoint.cs

internal static class RegisterPatientEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/patients", Handle)
            .WithName("RegisterPatient")
            .WithTags("Patients")
            .WithSummary("Register a new patient at this clinic")
            .AddEndpointFilter<ValidationFilter<RegisterPatientCommand>>()
            .Produces<RegisterPatientResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> Handle(
        RegisterPatientCommand command,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/patients/{result.Value.PatientId}", result.Value)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.Error!.Code] = [result.Error.Message],
            });
    }
}
```

The endpoint is `internal static`. It is not a service, it is not injected, it is not tested directly. Its only job is to translate HTTP ↔ mediator. All logic lives in the handler.

`AddEndpointFilter<ValidationFilter<RegisterPatientCommand>>()` wires the FluentValidation step for this specific endpoint. The generic filter from Part 1 handles the rest.

On success we return `201 Created` with a `Location` header pointing to the new resource. On failure we return a `ValidationProblem` using the error code and message from `Result.Error`. The client gets a structured problem response, not a stack trace.

---

## The read side: GetPatientById

A module with only commands is incomplete. You need to show the read side to demonstrate tenant isolation — and that is where the real payoff of `BaseDbContext` becomes visible.

### The contract

```csharp
// Patients.Contracts/GetPatientById.cs

public sealed record GetPatientByIdQuery(Guid PatientId) : IRequest<Result<GetPatientByIdResponse>>;

public sealed record GetPatientByIdResponse(
    Guid PatientId,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string ContactPhone,
    bool ConsentToCommunications);
```

Notice what is absent from `GetPatientByIdResponse`: `TenantId`, `CreatedAt`, `IsDeleted`. Those are infrastructure fields. The API response contains only the clinical data the caller is allowed to see.

### The handler

```csharp
// Features/GetPatientById/GetPatientByIdHandler.cs

public sealed class GetPatientByIdHandler(IDbContextFactory<PatientsDbContext> dbFactory)
    : IRequestHandler<GetPatientByIdQuery, Result<GetPatientByIdResponse>>
{
    public async ValueTask<Result<GetPatientByIdResponse>> Handle(
        GetPatientByIdQuery query,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var patient = await db.Patients
            .AsNoTracking()
            .Where(p => p.Id == query.PatientId)
            .Select(p => new GetPatientByIdResponse(
                p.Id,
                p.FirstName,
                p.LastName,
                p.DateOfBirth,
                p.ContactPhone,
                p.ConsentToCommunications))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return patient is null
            ? Result<GetPatientByIdResponse>.Fail(new Error("Patient.NotFound", $"Patient {query.PatientId} not found."))
            : Result<GetPatientByIdResponse>.Ok(patient);
    }
}
```

The query is `db.Patients.Where(p => p.Id == query.PatientId)`. There is no `&& p.TenantId == currentTenant`. That filter is not missing — it is structural.

When `BaseDbContext.OnModelCreating` runs, it finds every entity type that is a subtype of `AuditableEntity` and calls:

```csharp
builder.Entity<TEntity>()
    .HasQueryFilter(e => !e.IsDeleted && e.TenantId == tenantContext.TenantId);
```

`Patient` is an `AuditableEntity`. The filter is registered once at startup and applied by EF Core to **every query** against `db.Patients` for the lifetime of that context. You physically cannot write a query that crosses tenant boundaries. Forgetting to add `.Where(p => p.TenantId == ...)` is not a bug you can make here.

The `phi-and-tenancy.md` rule file says:

> BAD: `db.Patients.Where(p => p.Id == id)` [with manual tenant check]  
> GOOD: `db.Patients.Where(p => p.Id == id)` [tenant filter applied by the global query filter — never remove it]

They look identical. The difference is the global filter makes the second one correct by construction.

### The endpoint

```csharp
// Features/GetPatientById/GetPatientByIdEndpoint.cs

internal static class GetPatientByIdEndpoint
{
    internal static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/patients/{id:guid}", Handle)
            .WithName("GetPatientById")
            .WithTags("Patients")
            .WithSummary("Get a patient record by ID")
            .Produces<GetPatientByIdResponse>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPatientByIdQuery(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}
```

If the patient exists in a *different* tenant's data, `FirstOrDefaultAsync` returns null — the global filter excluded it before EF Core even generated the SQL. The response is a 404. To the caller it looks like the record does not exist, which is exactly the right behaviour. You do not tell a caller from Clinic A that a patient exists but belongs to Clinic B.

---

## Module registration

Both endpoints are registered in `PatientsModule.cs`:

```csharp
[assembly: MedClinicModule(typeof(PatientsModule), order: 10)]

namespace Patients;

public sealed class PatientsModule : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<PatientsDbContext>((sp, options) =>
            options.UseNpgsql(
                configuration["ConnectionStrings:DefaultConnection"],
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "patients")));

        services.AddValidatorsFromAssemblyContaining<RegisterPatientValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RegisterPatientEndpoint.Map(app);
        GetPatientByIdEndpoint.Map(app);
    }
}
```

`AddDbContextFactory<PatientsDbContext>` — not `AddDbContext`. This is required because handlers are singletons and DbContext is scoped. The factory solves the lifetime mismatch.

`AddValidatorsFromAssemblyContaining<RegisterPatientValidator>` scans the Patients assembly and registers all FluentValidation validators. One call, all validators.

The `[assembly: MedClinicModule(...)]` attribute is how the composition root in `MedClinic.Api` discovers modules at startup without a hard project reference to each one. It scans loaded assemblies for this attribute and calls `RegisterServices` and `MapEndpoints` on each.

---

## The phi-review workflow

Every time we add code that touches patient data, we run the `phi-review` workflow from `.agents/workflows/`. The workflow has the AI check three things:

**1. Are PHI fields appearing in log messages?**

Looking at the handler — no log statements at all. That is correct. The audit trail is written to the database, not to the log. The log does not know patient names, dates of birth, or phone numbers.

**2. Are queries tenant-scoped?**

The `GetPatientById` handler queries `db.Patients` with no explicit tenant filter. As established above, the global query filter in `BaseDbContext` handles this. The review confirms the filter is in place and has not been removed from `OnModelCreating`.

**3. Does every patient write produce an audit entry?**

The `RegisterPatient` handler writes an `AuditEntry` in the same `SaveChangesAsync` call as the patient record. They are atomic — both succeed or both fail. The review confirms audit entries are not conditional and not deferred.

The `phi-review` is not a checklist you tick off and forget. It is a structured prompt that the AI agent runs against the diff every time patient-data code changes. It catches the class of bugs that are easy to miss in code review: a log.Information call with a name interpolated in, a query filter that got removed to "fix a test", an audit entry that got wrapped in an `if` to "optimise writes".

---

## What the Patients module establishes

By the end of Part 2, the codebase has:

- A domain aggregate (`Patient`) that enforces consent at construction time and encapsulates all mutations behind named methods
- A command slice (`RegisterPatient`) that validates, creates, audits, and returns a typed result — no exceptions, no bare strings
- A query slice (`GetPatientById`) that is tenant-isolated by construction and exposes only the fields callers are allowed to see
- A module boundary (`Patients.Contracts`) that is enforced at compile time — other modules can reference the shapes, nothing else

Every module from Part 3 onwards copies this pattern. Appointments has an `AppointmentsModule`, a `BookAppointment` command, a `GetAppointmentById` query. Encounters has the same. The structure is not clever — that is the point. Every developer, human or AI, knows exactly where to look and exactly what a new feature looks like.

---

## Next: Part 3 — Appointments

Part 3 builds the Appointments module. It introduces two new concepts:

- **Cross-module queries** — `BookAppointment` needs to verify the patient exists, but Appointments cannot reference the Patients runtime project. It sends a `PatientExistsQuery` through Mediator using only the Contracts reference.
- **Aggregate state machines** — an appointment moves through `Scheduled → CheckedIn → Completed → Cancelled`. Those transitions live on the entity, not in handlers. Calling `appointment.CheckIn()` on an already-cancelled appointment returns a failure; the handler does not need a guard for every illegal state.

The `article/part-3` tag will capture that diff when it lands.

---

*Code for this article: `git checkout article/part-2`*  
*Previous: Part 1 — Building Blocks*  
*Next: Part 3 — The Appointments Module*
