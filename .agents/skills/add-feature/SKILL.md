---
name: add-feature
description: >
  Use when adding a backend vertical slice to a module — a new command or query
  for patients, appointments, encounters, prescriptions, etc. Triggers on "add
  feature", "create endpoint", "register a patient", "schedule an appointment",
  "check in", "cancel".
---

# Add a vertical-slice feature

Read first: `api-conventions.md`, `database.md`, and (for patient/clinical data) `phi-and-tenancy.md`.

Place files per `architecture.md` — one folder under `Features/<FeatureName>/`.

1. **Contracts** (`<Module>.Contracts`): a `record Command` or `record Query`, and a `record Response`.
   No EF types in Contracts.
2. **Handler** (`Features/<FeatureName>/<FeatureName>Handler.cs`): `public sealed`, returns
   `ValueTask<Result<T>>`, injects the module `DbContext`, `.ConfigureAwait(false)`, propagates the
   `CancellationToken`, stays tenant-scoped, calls aggregate methods for state changes.
3. **Validator** (`<FeatureName>Validator.cs`): FluentValidation. Required for every command and
   paginated query.
4. **Endpoint** (`<FeatureName>Endpoint.cs`): thin Minimal API with OpenAPI metadata + validation
   filter; mapped from the module's `MapEndpoints()`.
5. If an entity changed, follow the `create-migration` skill.
6. **Verify:** `dotnet build`; `dotnet test`; if patient/clinical data was touched, the `phi-review`
   workflow. The slice is done only when the build is clean and tests are green.
