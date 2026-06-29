# MedClinic — AI contributor guide

## Stack
.NET 10, Minimal APIs, EF Core 10, source-generated **Mediator** (NOT MediatR),
FluentValidation, PostgreSQL, Finbuckle multitenancy, Serilog, Scalar.

## Architecture
Modular monolith + Vertical Slice. Modules under src/Modules/<Name>/.
Each module has a runtime project and a <Name>.Contracts project.
Runtime modules NEVER reference each other — only each other's Contracts.

## Hard rules (always apply)
- Return Result<T> from handlers; don't throw for expected failures.
- Inject the module's DbContext directly; no repository wrappers.
- Propagate CancellationToken through every async call.
- TimeProvider.GetUtcNow(), never DateTime.Now.
- Minimal API endpoints are thin; logic lives in the Mediator handler.
- sealed handlers, file-scoped namespaces, primary constructors.

## Health-domain rules (NON-NEGOTIABLE — see .agents/rules)
- Every patient/clinical query MUST be tenant-scoped. No cross-clinic reads.
- NEVER log PHI (names, DOB, contact, diagnoses) in plaintext. Redact/hash.
- Every read or write of a medical record MUST emit an audit entry.

## Build & verify
- Build: dotnet build   ·   Test: dotnet test
- After any feature: build, then run tests, then self-review against .agents/rules.

## How to work here
- Match a task to a skill in .agents/skills and follow it.
- One vertical slice per change. Keep the diff reviewable.