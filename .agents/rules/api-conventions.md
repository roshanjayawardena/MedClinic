# API conventions — endpoints, CQRS, validation

Read this before adding an endpoint or a handler.

## The slice

A feature is a Command/Query (in `.Contracts`) + a handler + a validator + a thin endpoint.

```csharp
// Contracts: records only, no EF types
public sealed record CheckInCommand(Guid AppointmentId);
public sealed record CheckInResponse(Guid Id, string Status);
```

## Handlers

- `public sealed`, return `ValueTask<Result<T>>` (or `ValueTask<Result>`), inject the module DbContext.
- `.ConfigureAwait(false)` on every await. Propagate the `CancellationToken`.
- Return `Result<T>.Fail(...)` for expected failures — don't throw. Throw only for truly exceptional cases.
- Stay tenant-scoped (see `phi-and-tenancy.md`). Never log PHI.

```csharp
public sealed class CheckInHandler(AppointmentsDbContext db)
{
    public async ValueTask<Result<CheckInResponse>> Handle(CheckInCommand cmd, CancellationToken ct)
    {
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == cmd.AppointmentId, ct)
                                        .ConfigureAwait(false);
        if (appt is null) return Result<CheckInResponse>.Fail("Appointment not found.");

        var transition = appt.CheckIn();                 // rule lives in the aggregate
        if (transition.IsFailure) return Result<CheckInResponse>.Fail(transition.Error!);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<CheckInResponse>.Ok(new CheckInResponse(appt.Id, appt.Status.ToString()));
    }
}
```

## Validators (required)

Every command handler and every paginated query handler **must** have a `{Name}Validator`
(FluentValidation). This is enforced by the architecture tests.

## Endpoints (thin)

- Minimal API only. The endpoint maps the route, attaches OpenAPI metadata, runs the validation filter,
  sends the command/query through Mediator, and translates `Result` → `TypedResults`.
- No business logic in the endpoint. Map it from the module's `MapEndpoints()`.
- Errors surface as RFC 9457 `ProblemDetails`.

```csharp
group.MapPost("/{id:guid}/check-in", CheckIn.Handle)
     .WithName("CheckInAppointment")
     .Produces<CheckInResponse>(StatusCodes.Status200OK)
     .ProducesValidationProblem();
```
