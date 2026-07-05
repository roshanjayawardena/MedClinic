---
name: query-patterns
description: >
  Reference for writing read queries — paginated lists, single-record lookups, cross-module
  queries via Contracts. Triggers on "list query", "paginated", "get appointments", "search patients".
---

# Query patterns reference

Read first: `database.md`, `api-conventions.md`.

## Single-record lookup

```csharp
// Contracts
public sealed record GetPatientQuery(Guid PatientId);
public sealed record PatientResponse(Guid Id, string FirstName, string LastName, DateOnly DateOfBirth);

// Handler — project to DTO, never return entity
public sealed class GetPatientHandler(PatientsDbContext db)
{
    public async ValueTask<Result<PatientResponse>> Handle(GetPatientQuery q, CancellationToken ct)
    {
        var patient = await db.Patients
            .AsNoTracking()
            .Where(p => p.Id == q.PatientId)       // tenant filter applied by BaseDbContext
            .Select(p => new PatientResponse(p.Id, p.FirstName, p.LastName, p.DateOfBirth))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return patient is null
            ? Result.Fail<PatientResponse>("Patient not found.")
            : Result.Ok(patient);
    }
}
```

Key rules:
- `.AsNoTracking()` on all reads.
- `.Select(...)` project to DTO — never return the entity.
- Pass `CancellationToken` to every EF async call.
- Return `Result.Fail` for not-found — never throw `NotFoundException`.

## Paginated list

```csharp
// Contracts — always include pagination params
public sealed record GetAppointmentsQuery(
    DateOnly Date,
    int Page = 1,
    int PageSize = 20);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);

// Handler
public sealed class GetAppointmentsHandler(AppointmentsDbContext db)
{
    public async ValueTask<Result<PagedResponse<AppointmentDto>>> Handle(
        GetAppointmentsQuery q, CancellationToken ct)
    {
        var query = db.Appointments
            .AsNoTracking()
            .Where(a => a.ScheduledAt.Date == q.Date.ToDateTime(TimeOnly.MinValue))
            .OrderBy(a => a.ScheduledAt);

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .Select(a => new AppointmentDto(a.Id, a.PatientId, a.ScheduledAt, a.Status.ToString()))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Ok(new PagedResponse<AppointmentDto>(
            items, total, q.Page, q.PageSize,
            (int)Math.Ceiling((double)total / q.PageSize)));
    }
}
```

Validator (required for paginated queries):
```csharp
public sealed class GetAppointmentsValidator : AbstractValidator<GetAppointmentsQuery>
{
    public GetAppointmentsValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
```

## Cross-module query (via Contracts)

When module B needs data owned by module A:

```csharp
// A.Contracts — the only allowed surface
public sealed record PatientExistsQuery(Guid PatientId) : IQuery<bool>;

// A runtime — handler (no cross-module reference, just handles its own query)
public sealed class PatientExistsHandler(PatientsDbContext db)
{
    public async ValueTask<Result<bool>> Handle(PatientExistsQuery q, CancellationToken ct)
        => Result.Ok(await db.Patients.AnyAsync(p => p.Id == q.PatientId, ct).ConfigureAwait(false));
}

// B runtime — sends the query via Mediator (references A.Contracts only)
var exists = await mediator.Send(new PatientExistsQuery(cmd.PatientId), ct);
if (!exists.Value) return Result.Fail<BookAppointmentResponse>("Patient not found.");
```

**Never** query another module's `DbContext` directly. Always go through Contracts + Mediator.

## PHI projection rule

When returning patient-related data in a cross-module context, project only the minimum fields the
caller is permitted to see. Never expose the full PHI set across module boundaries.
