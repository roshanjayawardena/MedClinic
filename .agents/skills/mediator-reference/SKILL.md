---
name: mediator-reference
description: >
  Reference for the source-generated Mediator (NOT MediatR). Use when wiring up
  handlers, understanding dispatch, or configuring assembly discovery.
---

# Source-generated Mediator reference

This project uses **Mediator** (Martinothamar/Mediator) — a source-generated alternative to MediatR.
The API looks similar but the internals are different. Do **not** use MediatR types.

## Key difference from MediatR

| MediatR | Mediator (source-gen) |
|---|---|
| `IRequest<T>` | `ICommand<T>` or `IQuery<T>` (or custom) |
| `IRequestHandler<TReq, TRes>` | `ICommandHandler<TCmd, TRes>` |
| `IMediator.Send(request)` | `IMediator.Send(command)` |
| Runtime reflection | Compile-time source generation |
| NuGet: MediatR | NuGet: Mediator |

In MediClinic we use plain records in Contracts and sealed handlers — the Mediator source generator
discovers handlers by scanning registered assemblies. **There is no base interface on the Command/Query records themselves.**

## Handler shape

```csharp
// The handler implements the generated interface
public sealed class RegisterPatientHandler(PatientsDbContext db, ILogger<RegisterPatientHandler> logger)
    : ICommandHandler<RegisterPatientCommand, Result<RegisterPatientResponse>>
{
    public async ValueTask<Result<RegisterPatientResponse>> Handle(
        RegisterPatientCommand command, CancellationToken cancellationToken)
    {
        // ... implementation
    }
}
```

## Assembly registration (CRITICAL)

The source generator must know which assemblies to scan. Missing an assembly = silent 404.

```csharp
// Program.cs
builder.Services.AddMediator(o =>
{
    o.Assemblies = new[]
    {
        // BuildingBlocks marker
        typeof(Result).Assembly,
        // Each module's runtime AND Contracts (handlers can be in either)
        typeof(PatientsModule).Assembly,
        typeof(RegisterPatientCommand).Assembly,    // Patients.Contracts
        typeof(AppointmentsModule).Assembly,
        typeof(BookAppointmentCommand).Assembly,    // Appointments.Contracts
        // ... add both projects for every new module
    };
});
```

Rule from `add-module` skill: **add both the runtime assembly and the Contracts assembly** when wiring
a new module. The Contracts assembly may contain cross-module query handlers.

## Sending from an endpoint

```csharp
// Thin endpoint — just sends, translates Result → HTTP
static async Task<IResult> Handle(
    RegisterPatientCommand command,
    IMediator mediator,
    CancellationToken ct)
{
    var result = await mediator.Send(command, ct);
    return result.IsSuccess
        ? TypedResults.Created($"/patients/{result.Value.PatientId}", result.Value)
        : TypedResults.BadRequest(result.Error);
}
```

## Sending from a handler (cross-module query)

```csharp
// BookAppointmentHandler sends a cross-module query via Mediator
var patientExists = await mediator.Send(new PatientExistsQuery(command.PatientId), ct)
                                  .ConfigureAwait(false);
if (!patientExists.Value)
    return Result.Fail<BookAppointmentResponse>("Patient not found.");
```

## Notifications (domain events)

```csharp
// Raise in-process (domain events, not cross-module)
await mediator.Publish(new PatientRegisteredEvent(patient.Id), ct).ConfigureAwait(false);
```

## Common mistakes

- ❌ Using `IRequest<T>` / `IRequestHandler<,>` — those are MediatR types; they won't compile.
- ❌ Forgetting to add both the runtime and Contracts assembly to `o.Assemblies` — silent 404.
- ❌ Adding `o.Assemblies` in `Api/Program.cs` but not in `DbMigrator/Program.cs` — migrator fails.
