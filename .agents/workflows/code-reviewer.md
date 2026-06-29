# code-reviewer (read-only)

Review the current diff against the project conventions and emit a structured report. **Do not modify code.**

Check, citing file + line:
- **Boundaries:** does any module reference another module's *runtime* (not `.Contracts`)?
- **Placement:** are files in the right feature folder (Domain/, Persistence/, Features/<Name>/) per `architecture.md`? Flag any flat file-type layout.
- **Handlers:** `public sealed`? return `ValueTask<Result<T>>`? `.ConfigureAwait(false)` on every await? `CancellationToken` propagated?
- **Validators:** does every command / paginated query have a `{Name}Validator`?
- **Endpoints:** thin (no business logic)? OpenAPI metadata + validation filter present?
- **Logging:** structured (message templates), never string interpolation?
- **State changes:** done via aggregate methods, not re-implemented in handlers?

Report format: a short list grouped Must-fix / Should-fix / Nit. End with a one-line verdict
(ready / needs changes). No code edits.
