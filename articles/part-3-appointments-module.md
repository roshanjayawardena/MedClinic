# State Machines in Practice: The Appointment Booking & Check-In Lifecycle

*Part 3 of Building a Production Medical Clinic SaaS with AI*

---

Part 2 built the Patients module and established the vertical slice pattern. Every future module follows that same structure: Contracts project, runtime project, domain aggregate, one folder per feature.

Part 3 puts two things into practice that Part 2 did not need: **aggregate state machines** and **cross-module communication**. An appointment is not a passive data record — it has a lifecycle. And booking an appointment requires verifying a patient exists, but the Appointments module must never import the Patients runtime project. Those two constraints shape everything in this article.

Everything here is at git tag `article/part-3`.

---

## What changed in the host before we wrote a line of Appointments code

When the Patients module was the only module, the Mediator source generator lived in `Patients.csproj`. At compile time, the generator scanned Patients' project graph, found `RegisterPatientHandler` and `GetPatientByIdHandler`, and emitted a dispatch table that knew about those two handlers.

That works for one module. It breaks for two. When `MedClinic.Api` references both `Patients` and `Appointments`, the generated dispatcher in `Patients.dll` has already been compiled — it does not know `BookAppointmentHandler` exists. `BookAppointmentCommand` would hit a runtime "no handler registered" error.

The fix is architectural: **the source generator belongs in the host**, not in each module. The host is the only project that sees all modules at compile time. Moving the generator there means the generated dispatcher includes every handler in every module the host references — automatically, without configuration.

```diff
// Patients.csproj — remove the generator, keep only the abstractions
- <PackageReference Include="Mediator.SourceGenerator" Version="3.0.2" ... />
+ <PackageReference Include="Mediator.Abstractions" Version="3.0.2" />

// MedClinic.Api.csproj — the generator now lives here
+ <PackageReference Include="Mediator.SourceGenerator" Version="3.0.2">
+   <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
+   <PrivateAssets>all</PrivateAssets>
+ </PackageReference>
```

Every module project has only `Mediator.Abstractions` — the interfaces for `IRequest<T>`, `IRequestHandler<,>`, and `INotification`. The host project has `Mediator.SourceGenerator` — the Roslyn analyzer that reads all those handler implementations and generates the zero-reflection dispatch class. This is the correct multi-module setup for the source-generated Mediator.

The `Program.cs` registration loop also graduates from individual module calls to an array:

```csharp
var modules = new IModule[]
{
    new PatientsModule(),
    new AppointmentsModule(),
};

foreach (var module in modules)
    module.RegisterServices(builder.Services, builder.Configuration);
// ... app builds ...
foreach (var module in modules)
    module.MapEndpoints(app);
```

Adding a new module in future parts is a two-line change: add it to this array, and add a project reference.

---

## The module boundary in practice

The `Appointments` runtime project references `Patients.Contracts`. Not `Patients`. The `.csproj` makes this explicit:

```xml
<!-- Appointments/Appointments.csproj -->
<ProjectReference Include="..\..\..\Modules\Patients\Patients.Contracts\Patients.Contracts.csproj" />
```

There is no reference to `Patients.csproj`. The build enforces it. If any code in the Appointments module tries to `using Patients.Domain;` or `using Patients.Persistence;`, it fails to compile. The only channel from Appointments into the Patients world is through `Patients.Contracts` — the commands, queries, and response types Patients has chosen to make public.

This is the boundary the `add-module` skill describes. It is not a convention enforced by code review. It is enforced by the compiler at every build.

---

## The Appointment aggregate

An appointment moves through a fixed set of states:

```
Scheduled → CheckedIn → Completed
    ↓            ↓
 Cancelled    Cancelled
```

Every transition is a method on the aggregate. There is no `Status = AppointmentStatus.CheckedIn` anywhere in a handler. The only way to move an appointment to `CheckedIn` is to call `appointment.CheckIn(now)`.

```csharp
// Domain/Appointment.cs

public sealed class Appointment : AuditableEntity
{
    private Appointment() { }

    public Guid PatientId { get; private set; }
    public DateTimeOffset ScheduledAt { get; private set; }
    public int DurationMinutes { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public AppointmentStatus Status { get; private set; }
    public DateTimeOffset? CheckedInAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public static Appointment Book(
        Guid patientId,
        DateTimeOffset scheduledAt,
        int durationMinutes,
        string reason) =>
        new()
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            ScheduledAt = scheduledAt,
            DurationMinutes = durationMinutes,
            Reason = reason,
            Status = AppointmentStatus.Scheduled,
        };

    public Result CheckIn(DateTimeOffset now)
    {
        if (Status != AppointmentStatus.Scheduled)
            return Result.Fail(new Error("Appointment.InvalidStatus",
                $"Cannot check in from status '{Status}'. Expected 'Scheduled'."));

        Status = AppointmentStatus.CheckedIn;
        CheckedInAt = now;
        return Result.Ok();
    }

    public Result Complete(DateTimeOffset now)
    {
        if (Status != AppointmentStatus.CheckedIn)
            return Result.Fail(new Error("Appointment.InvalidStatus",
                $"Cannot complete from status '{Status}'. Expected 'CheckedIn'."));

        Status = AppointmentStatus.Completed;
        CompletedAt = now;
        return Result.Ok();
    }

    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (Status is AppointmentStatus.Completed or AppointmentStatus.Cancelled)
            return Result.Fail(new Error("Appointment.InvalidStatus",
                $"Cannot cancel an appointment with status '{Status}'."));

        Status = AppointmentStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = now;
        return Result.Ok();
    }
}
```

**Why does `CheckIn()` live on the entity and not in the handler?**

The handler would look like:

```csharp
// Handler approach (wrong)
if (appointment.Status != AppointmentStatus.Scheduled)
    return Result.Fail(...);
appointment.Status = AppointmentStatus.CheckedIn;
appointment.CheckedInAt = now;
```

The entity approach:

```csharp
// Entity approach (right)
var result = appointment.CheckIn(timeProvider.GetUtcNow());
if (result.IsFailure)
    return Result<CheckInAppointmentResponse>.Fail(result.Error!);
```

Three reasons the entity approach wins.

First, **the guard is where the state is**. The entity knows its own valid transitions. If `Status` is `Cancelled`, calling `CheckIn()` returns a failure — always, regardless of which handler called it. There is no way to forget to add the guard in a second handler that also touches status.

Second, **the aggregate is testable in isolation**. You can unit-test `appointment.CheckIn()` without a database, without a handler, without FluentValidation. You construct an `Appointment` in `Cancelled` state and assert the transition fails.

Third, **the intent is explicit**. `appointment.CheckIn(now)` reads as a business operation. `appointment.Status = AppointmentStatus.CheckedIn` reads as data manipulation. The difference matters when you are reading the code six months later.

---

## BookAppointment: cross-module query and double-booking guard

The `BookAppointment` handler does two things before touching the database: it verifies the patient exists, and it checks for overlapping appointments.

```csharp
// Features/BookAppointment/BookAppointmentHandler.cs

public sealed class BookAppointmentHandler(
    IDbContextFactory<AppointmentsDbContext> dbFactory,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    IMediator mediator)
    : IRequestHandler<BookAppointmentCommand, Result<BookAppointmentResponse>>
{
    public async ValueTask<Result<BookAppointmentResponse>> Handle(
        BookAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        // Cross-module query — Contracts reference only, never the Patients runtime project.
        var patientResult = await mediator.Send(
            new PatientExistsQuery(command.PatientId), cancellationToken);

        if (!patientResult.Value)
            return Result<BookAppointmentResponse>.Fail(
                new Error("Patient.NotFound",
                    $"Patient {command.PatientId} does not exist in this clinic."));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Double-booking guard — business rule in the handler, not a DB constraint.
        var endTime = command.ScheduledAt.AddMinutes(command.DurationMinutes);

        var hasOverlap = await db.Appointments
            .AnyAsync(a =>
                a.Status != AppointmentStatus.Cancelled &&
                a.ScheduledAt < endTime &&
                a.ScheduledAt.AddMinutes(a.DurationMinutes) > command.ScheduledAt,
                cancellationToken)
            .ConfigureAwait(false);

        if (hasOverlap)
            return Result<BookAppointmentResponse>.Fail(
                new Error("Appointment.DoubleBooking",
                    $"The clinic already has an appointment between {command.ScheduledAt:g} and {endTime:g}."));

        var appointment = Appointment.Book(
            command.PatientId,
            command.ScheduledAt,
            command.DurationMinutes,
            command.Reason);

        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Integration event — see the eventing section below.
        await mediator.Publish(
            new AppointmentBookedIntegrationEvent(
                appointment.Id,
                appointment.PatientId,
                tenantContext.TenantId,
                appointment.ScheduledAt,
                timeProvider.GetUtcNow()),
            cancellationToken)
            .ConfigureAwait(false);

        return Result<BookAppointmentResponse>.Ok(new BookAppointmentResponse(appointment.Id));
    }
}
```

### The cross-module query

`PatientExistsQuery` is defined in `Patients.Contracts`. The handler for it — `PatientExistsHandler` — lives in the `Patients` runtime project. The Appointments handler sends the query via `IMediator` and gets back `Result<bool>`.

At runtime, the generated dispatcher in `MedClinic.Api` knows that `PatientExistsQuery` maps to `PatientExistsHandler`. The Appointments handler never has a direct call to any class in the Patients runtime project. It does not import a `PatientService`, it does not inject a `PatientsDbContext`, it does not call any method on any Patients class. It sends a message and receives an answer.

This is the contract-only boundary in practice. If the Patients team renames their handler, reorganises their folder structure, or changes their DbContext — none of that breaks the Appointments module. The contract (`PatientExistsQuery` → `Result<bool>`) is the only thing Appointments depends on.

### The double-booking guard

The clinic has one doctor. Two appointments cannot overlap. This is a business rule, and it lives in the handler.

The overlap check uses interval arithmetic: two time intervals `[A.start, A.end)` and `[B.start, B.end)` overlap when `A.start < B.end && B.start < A.end`. The query:

```csharp
a.ScheduledAt < endTime &&
a.ScheduledAt.AddMinutes(a.DurationMinutes) > command.ScheduledAt
```

translates directly to that predicate. It excludes `Cancelled` appointments — a slot that was cancelled is available again.

**Why a business rule and not a database unique constraint?**

A unique constraint can enforce uniqueness of a value — you cannot have two appointments with the same ID. It cannot enforce an interval overlap condition. The database has no way to express "no two non-cancelled rows where the time intervals intersect" as a constraint without a trigger or a check constraint that spans multiple rows, and those approaches are fragile.

The business rule in the handler is explicit, testable, and readable. The tradeoff is that under concurrent requests you could have a race condition: two requests read "no overlap" simultaneously and both proceed. For a single-doctor clinic running serialized appointment slots, this is an acceptable tradeoff. A clinic running at hospital scale would add a database-level serializable transaction here. We choose the simpler path because simplicity is a feature of this system.

There is also something important to notice about the overlap query itself: it only checks appointments for the current tenant. The global query filter on `AppointmentsDbContext` — inherited from `BaseDbContext` — ensures `db.Appointments` never returns rows from a different clinic. The double-booking check is scoped to this clinic automatically.

---

## The CheckInAppointment feature

The check-in handler loads the appointment, delegates the transition to the aggregate, and saves.

```csharp
// Features/CheckInAppointment/CheckInAppointmentHandler.cs

public sealed class CheckInAppointmentHandler(
    IDbContextFactory<AppointmentsDbContext> dbFactory,
    TimeProvider timeProvider)
    : IRequestHandler<CheckInAppointmentCommand, Result<CheckInAppointmentResponse>>
{
    public async ValueTask<Result<CheckInAppointmentResponse>> Handle(
        CheckInAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, cancellationToken)
            .ConfigureAwait(false);

        if (appointment is null)
            return Result<CheckInAppointmentResponse>.Fail(
                new Error("Appointment.NotFound", $"Appointment {command.AppointmentId} not found."));

        var result = appointment.CheckIn(timeProvider.GetUtcNow());
        if (result.IsFailure)
            return Result<CheckInAppointmentResponse>.Fail(result.Error!);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CheckInAppointmentResponse>.Ok(
            new CheckInAppointmentResponse(appointment.Id, appointment.Status.ToString()));
    }
}
```

The handler has three responsibilities: load, transition, save. It has zero knowledge of what a valid transition looks like. If the appointment is `Cancelled`, `appointment.CheckIn(now)` returns `Result.Fail(...)` and the handler propagates that failure up to the endpoint as a 422.

The endpoint maps the error code to the appropriate HTTP status:

```csharp
return result.IsSuccess
    ? TypedResults.Ok(result.Value)
    : result.Error!.Code == "Appointment.NotFound"
        ? TypedResults.NotFound()
        : TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [result.Error.Code] = [result.Error.Message],
        });
```

`Appointment.NotFound` → 404. `Appointment.InvalidStatus` → 422 ValidationProblem. The endpoint pattern-matches on the error code because HTTP status selection is presentation logic, not business logic. The handler returns a typed error; the endpoint decides what that means for the HTTP response.

---

## Integration events: AppointmentBooked → Notifications

When an appointment is booked successfully, the Notifications module needs to know so it can schedule a reminder. But the BookAppointment handler cannot call Notifications directly — that would be the same cross-module violation we just avoided with PatientExistsQuery.

The solution is an integration event published by the handler, subscribed to by the Notifications module.

### Defining the event

The event lives in `Appointments.Contracts`:

```csharp
// Appointments.Contracts/Events/AppointmentBookedIntegrationEvent.cs

public sealed record AppointmentBookedIntegrationEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ClinicId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset OccurredAt) : INotification;
```

Three things to call out.

`INotification` — this is the Mediator source-generator interface for publish/subscribe. Just as `IRequest<T>` has exactly one handler, `INotification` can have zero or more handlers. Publishing fires all registered handlers.

No PHI in the payload. The event carries `AppointmentId`, `PatientId`, `ClinicId`, and timestamps. It does not carry the patient's name, the reason for the visit, or any clinical detail. If the Notifications module needs those fields, it looks them up from its own data or queries the relevant Contracts. Events are identifiers and timestamps, not data bundles.

`ClinicId` is included. This is required so the event relay can restore the correct tenant context when the subscriber handles the event. An event that loses its tenant context is a tenant isolation violation.

### Publishing the event

The handler publishes after `SaveChangesAsync`:

```csharp
db.Appointments.Add(appointment);
await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

await mediator.Publish(
    new AppointmentBookedIntegrationEvent(
        appointment.Id,
        appointment.PatientId,
        tenantContext.TenantId,
        appointment.ScheduledAt,
        timeProvider.GetUtcNow()),
    cancellationToken)
    .ConfigureAwait(false);
```

**A note on delivery guarantees.** In this implementation, the event is published in-process via Mediator's `IPublisher`. It is synchronous: when `mediator.Publish()` returns, all subscribers have handled it. This is simple and works perfectly for the current scale of the clinic.

What it does not provide is durability. If the handler crashes between `SaveChangesAsync` and `mediator.Publish`, the appointment is saved but the event is never delivered. The Notifications module never schedules the reminder.

The production solution is the Outbox pattern: write the event as a database row in the same transaction as the appointment, then have a background relay deliver it asynchronously. Part 8 (Notifications) will introduce this. For now, in-process delivery is the right starting point — it keeps the implementation simple and demonstrable. Swapping in the Outbox later does not change the handler code; it changes only how `mediator.Publish` is backed.

### The add-integration-event skill

The `.agents/skills/add-integration-event/SKILL.md` skill captures the full recipe for cross-module events:

1. Define the event in the publisher's `.Contracts` (not the subscriber's — the publisher owns the shape)
2. Publish inside the handler in the same unit of work
3. Add a `ProjectReference` from the subscriber's runtime to the publisher's Contracts
4. Write an idempotent subscriber handler with an existence check
5. Update the event catalogue in `eventing.md`

For any AI agent working on this codebase, that skill is the prompt. When Cursor or Claude Code sees "add the AppointmentCancelled integration event", the skill tells it exactly where each piece goes and what rules apply. The `phi-and-tenancy.md` rule is explicitly listed as a dependency because event payloads are the most common place PHI leaks across module boundaries — a developer adds "just the patient name for the notification" and suddenly the event log contains PHI.

---

## The DbContext and migration

`AppointmentsDbContext` follows the same pattern as `PatientsDbContext`:

```csharp
// Persistence/AppointmentsDbContext.cs

public sealed class AppointmentsDbContext(
    DbContextOptions<AppointmentsDbContext> options,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : BaseDbContext<AppointmentsDbContext>(options, tenantContext, timeProvider)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("appointments");

        modelBuilder.Entity<Appointment>(a =>
        {
            a.ToTable("appointments");
            a.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            a.Property(x => x.CancellationReason).HasMaxLength(500);
            a.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            a.HasIndex(x => x.TenantId);
            a.HasIndex(x => new { x.TenantId, x.ScheduledAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
```

`HasDefaultSchema("appointments")` — each module owns its own PostgreSQL schema. The `patients` schema holds patient tables. The `appointments` schema holds appointment tables. They never share tables. If we ever split the monolith into separate services, each schema is already an isolated unit.

`Status` is stored as a `string` via `HasConversion<string>()`. This makes the database column human-readable (`'Scheduled'`, `'CheckedIn'`, etc.) rather than an opaque integer. Adding a new status value in the future does not require a data migration — the column just starts receiving a new string.

The composite index `(TenantId, ScheduledAt)` supports the overlap query. The overlap check filters by `ScheduledAt` and `ScheduledAt.AddMinutes(DurationMinutes)`, and `TenantId` is in the global query filter. A scan without this index would read every appointment in the table for the tenant — for a busy clinic that could be thousands of rows. The index makes it a range scan on a small slice.

The migration is generated with:

```bash
dotnet ef migrations add InitialAppointmentsCreate \
  --context AppointmentsDbContext \
  --output-dir Migrations/Appointments \
  --project src/Host/MedClinic.Migrations.PostgreSQL
```

The `AppointmentsDesignTimeFactory` in the Migrations project tells EF how to construct the `AppointmentsDbContext` without a running application:

```csharp
internal sealed class AppointmentsDesignTimeFactory : IDesignTimeDbContextFactory<AppointmentsDbContext>
{
    public AppointmentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppointmentsDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5433;Database=mediclinic_dev;Username=postgres;Password=postgres",
                npg => npg
                    .MigrationsAssembly("MedClinic.Migrations.PostgreSQL")
                    .MigrationsHistoryTable("__EFMigrationsHistory", "appointments"))
            .Options;

        return new AppointmentsDbContext(options, new MigrationTenantContext(), TimeProvider.System);
    }
}
```

`DbMigrator` applies Appointments migrations immediately after Patients:

```csharp
await sp.GetRequiredService<PatientsDbContext>().Database.MigrateAsync();
Console.WriteLine("  ✓ Patients");

await sp.GetRequiredService<AppointmentsDbContext>().Database.MigrateAsync();
Console.WriteLine("  ✓ Appointments");
```

Order matters. Appointments can reference patient IDs as foreign keys in future parts. Patients schema must exist before Appointments schema.

---

## Endpoints delivered in Part 3

| Method | Route | Handler |
|---|---|---|
| `POST` | `/appointments` | `BookAppointmentHandler` |
| `POST` | `/appointments/{id}/check-in` | `CheckInAppointmentHandler` |
| `GET` | `/appointments/{id}` | `GetAppointmentByIdHandler` |

The full module adds up to exactly the same structure as Patients: one folder per feature, one contract per feature, thin endpoints that translate HTTP to mediator and back. The patterns established in Part 2 replicate without modification.

---

## What Part 3 established

- The Mediator source generator belongs in the host, not in each module. Moving it there means any module the host references automatically has its handlers dispatched — no registration per handler, no reflection, no configuration.
- State machine methods on the aggregate (`CheckIn()`, `Complete()`, `Cancel()`) are the only path to a state change. The guard and the mutation are co-located.
- Cross-module queries go through Contracts only. `BookAppointmentHandler` sends `PatientExistsQuery` via `IMediator` and has zero compile-time knowledge of how Patients implements the answer.
- Integration events are defined in the publisher's Contracts, carry no PHI, and always include a `ClinicId` for tenant context preservation.

---

## Next: Part 4 — Encounters

Part 4 builds the Encounters module: clinical notes, ICD-10 diagnoses, and vital signs. It introduces owned entities in EF Core (a `Diagnosis` value object inside an `Encounter`), the audit trail requirement for every clinical read and write, and the `EncounterClosedIntegrationEvent` that Prescriptions depends on before a script can be written.

*Code for this article: `git checkout article/part-3`*  
*Previous: Part 2 — The Patients Module*  
*Next: Part 4 — The Encounters Module*
