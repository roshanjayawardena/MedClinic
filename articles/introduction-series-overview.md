# Building a Production Medical Clinic SaaS with AI — Series Introduction

*A 10-part guide to building MediClinic: a real-world, multi-tenant, PHI-safe clinic management system on .NET 10, built entirely with Claude AI assistance*

---

## Why this series exists

Most software tutorials lie by omission.

They show you a `ProductsController` with four methods. A `DbContext` with two tables. A README that says "clone and run." Everything works. Everything is clean. And then you try to build something real on top of it — multi-tenancy, role-based permissions, audit trails, cross-module communication, PHI compliance — and it all falls apart, because the foundation was never designed to carry that weight.

This series builds something real, in public, explaining every decision as it's made.

**MediClinic** is a production-grade SaaS for private medical clinics: patient registration, appointment scheduling, clinical encounters, prescriptions, billing, and notifications — across multiple fully-isolated tenants, with PHI protection at every layer. The domain is small enough to build in ten articles and realistic enough to surface every problem that matters.

There's a second thread running through this series that most technical writing ignores: **how AI assistance changes the way software is built.** Every module in MediClinic was built with Claude Code as a collaborator. The repository contains a complete AI development layer — `AGENTS.md`, skills, workflows, review gates — and Part 10 documents how it works and why it makes AI assistance reliable rather than risky.

If you want to learn .NET modular monolith architecture in a real domain, start at Part 1. If you want to understand how to build a codebase that an AI can contribute to without introducing data breaches, read Part 10 first and then come back.

Both paths are in here.

---

## The domain: a private medical clinic

MediClinic is a clinic management system for a **single-doctor private practice.** One GP. One pharmacist who also handles front-desk duties. A known, small patient population.

That scope is intentional. Large enough to surface real engineering problems. Small enough to build end-to-end in a realistic time frame.

The system covers:

- **Patient registration** — demographics, explicit consent, allergy records
- **Appointment scheduling** — booking, check-in, cancellation with status lifecycle
- **Clinical encounters** — the doctor's consultation notes, ICD-10 diagnoses, vitals
- **Prescriptions** — the doctor writes them; the pharmacist dispenses them
- **Billing** — consultation fees, invoices triggered by clinical events, payment tracking
- **Notifications** — appointment reminders, payment confirmations (consent-gated)

And it does all of this across multiple independent clinics — **multi-tenant**, with complete data isolation between clinics.

### The PHI reality

Healthcare software handles **Protected Health Information**: patient names, dates of birth, diagnoses, medications, contact details. These have legal protection in most jurisdictions — GDPR in Europe, HIPAA in the US, PDPA in parts of Asia. Violating these rules isn't just a compliance failure; it's a breach of patient trust.

This shapes every design decision in the codebase:

- Patient names **never appear in log files**, even at debug level
- Every read or write of a clinical record emits an **immutable audit trail**
- Patient data belongs to exactly one clinic — **no cross-tenant reads, ever**
- Patients give **explicit consent** before their data is processed or they're contacted

These aren't afterthoughts bolted on at the end. They're foundations laid in Part 1, expressed in code by Part 2, and mechanically enforced by Part 9.

---

## The architecture in three paragraphs

**Modular monolith.** Seven modules deployed as a single process: Patients, Appointments, Encounters, Prescriptions, Identity, Billing, and Notifications. Each module owns its own `DbContext`, its own domain entities, and its own feature folders. Modules communicate only through their `.Contracts` projects — never through runtime project references. A module boundary violation is a build-time error, caught by architecture tests before it reaches production.

**Vertical slices.** Inside each module, code is organized by feature, not by layer. There's no `Services/` folder, no `Repositories/` folder, no `Controllers/` folder. There's `Features/BookAppointment/` containing the command, handler, validator, and endpoint — everything needed to understand and modify the feature in one place.

**Default-safe infrastructure.** The `BaseDbContext` applies global EF Core query filters to every entity, scoping every query to the current tenant automatically. `TimeProvider` replaces `DateTime.UtcNow` everywhere, making time testable. `Result<T>` replaces exception-based control flow, making failures explicit. The infrastructure makes the correct behavior the path of least resistance.

### The module map

```
src/Modules/
├── Patients/        — Demographics, consent, allergies (the reference module)
├── Appointments/    — Booking, check-in, status lifecycle
├── Encounters/      — Clinical notes, diagnoses, vitals
├── Prescriptions/   — Drug orders written by Doctor, dispensed by Pharmacist
├── Identity/        — JWT auth, three roles, fine-grained permissions
├── Billing/         — Invoices triggered by clinical events
└── Notifications/   — Appointment reminders and payment confirmations
```

Each module is two projects:

| Project | Purpose |
|---|---|
| `<Module>/` | Runtime: `Domain/`, `Persistence/`, `Features/`, module registration |
| `<Module>.Contracts/` | Public surface: Commands, Queries, Responses, integration events |

### The tech stack

| Layer | Choice | Why |
|---|---|---|
| Runtime | .NET 10 | LTS, performance, modern C# 13 features |
| API | Minimal APIs | Thin by design; no controller boilerplate |
| Mediator | Source-generated Mediator | Compile-time dispatch, zero reflection at runtime |
| Validation | FluentValidation 11 | Expressive, DI-friendly, testable in isolation |
| ORM | EF Core 10 | Global query filters = tenant isolation by default |
| Database | PostgreSQL 16 | Production-grade, strong .NET ecosystem |
| Multi-tenancy | Finbuckle.MultiTenant | Header-based resolution, purpose-built for .NET |
| Auth | ASP.NET Core Identity + JWT | Standard, auditable, no vendor lock-in |
| Logging | Serilog | Structured output; PHI-safe template discipline |
| API docs | Scalar | Modern replacement for Swagger UI |
| Testing | xUnit + Testcontainers + NetArchTest | Real PostgreSQL; no mocked DbContexts |

One detail worth calling out: **source-generated Mediator, not MediatR.** The source generator creates dispatch code at compile time, so there's no runtime assembly scanning. The API looks similar to MediatR, but it's a different library with different handler interfaces. Part 1 covers the distinction.

---

## The 10 non-negotiable rules

Every decision in MediClinic flows from ten rules stated in `AGENTS.md` — the repository's canonical guide for both human and AI contributors:

```
1.  Return Result<T> from handlers; never throw for expected failures.
2.  Inject the module's own DbContext; no repository wrappers.
3.  Propagate CancellationToken through every async call.
4.  TimeProvider.GetUtcNow() — never DateTime.Now or DateTime.UtcNow directly.
5.  Minimal API endpoints are thin; all logic lives in the Mediator handler.
6.  sealed handlers, file-scoped namespaces, primary constructors throughout.
7.  Every patient/clinical query MUST be tenant-scoped. No cross-clinic reads.
8.  NEVER log PHI (names, DOB, contact info, diagnoses) in plaintext.
9.  Every read or write of an Encounter or Prescription MUST emit an audit entry.
10. Modules reference only each other's .Contracts — never the runtime project.
```

Rules 1–6 are mechanical conventions — learnable by imitation, verifiable at code review. Rules 7–10 are safety invariants — they require understanding *why*, not just *what*, and failing them in a medical system has real consequences.

By Part 9, all ten rules are enforced mechanically: architecture tests check rule 10 at build time; integration tests verify rule 7 against a real database; the `phi-review` workflow gates every clinical feature on rules 8 and 9.

---

## How Claude AI built this system

Every module in this series was built with Claude Code as a primary contributor, not a suggestion engine.

That distinction matters. Using AI as a suggestion engine means reading suggestions and deciding which ones to accept. Using it as a contributor means giving it a task specification, letting it execute, and reviewing the result — the same workflow as a junior developer on the team.

For that to work reliably on a medical SaaS, the codebase needed an AI interface — explicit, tool-neutral specifications for what "correct" looks like in this system. The `.agents/` directory is that interface.

### The `.agents/` directory

```
.agents/
├── rules/         — Convention files, loaded on demand per task type
├── skills/        — Task recipes: step-by-step instructions for repeatable tasks
└── workflows/     — Orchestration: sequences of skills with review gates
```

**Rules** capture non-obvious conventions — things a senior developer knows but a new contributor doesn't:

| File | When it's loaded |
|---|---|
| `architecture.md` | Any module creation or file placement question |
| `api-conventions.md` | Adding any endpoint or handler |
| `database.md` | Adding an entity, query, or migration |
| `phi-and-tenancy.md` | Anything touching patient or clinical data |
| `auditing.md` | Encounters, Prescriptions, any clinical record access |
| `eventing.md` | Cross-module integration events |
| `security.md` | Auth, permissions, rate limiting |
| `testing.md` | Writing unit or integration tests |
| `modules/encounters.md` | Working in the Encounters module specifically |

Loading only the rules relevant to the current task keeps the AI's context focused. Working on a Billing feature doesn't require reading the Prescriptions module conventions.

**Skills** are task recipes — the complete specification for one repeatable task:

- `add-feature` — creates the command, handler, validator, endpoint, and verifies with a build + test gate
- `add-entity` — creates the domain type, EF configuration, DbSet, and migration
- `add-module` — wires a new bounded context into every registration site (the silent footgun if missed)
- `create-migration` — covers all four required flags and a review checklist for the generated SQL
- `add-integration-event` — publisher contracts, Outbox publish, subscriber with idempotency guard
- `add-permission` — permission constant in Contracts, role assignment, endpoint guard, test

**Workflows** sequence skills with mandatory review gates:

- `feature-scaffolder` — entity → feature → migration → tests → `code-reviewer` → `phi-review`
- `architecture-guard` — read-only boundary compliance report (violations block merge)
- `phi-review` — read-only PHI and tenant safety gate (must pass before any clinical feature ships)

### The prompt pattern that made this work

The difference between a productive AI session and an hour of back-and-forth is how the task is framed:

```
[Task] in the [Module] module.
Follow the [skill name] skill in `.agents/skills/`.
Load [relevant rule files] before writing any code.
Done means: dotnet build clean, dotnet test green, [review workflow] passes.
```

The "done means" clause is the highest-leverage part. Without it, AI reports completion when the happy path compiles. With it, it knows to run the build, run the tests, and run the review gate before reporting back.

### What AI got right — and where humans still reviewed

The series wouldn't be honest without naming both sides of this.

**AI contributed reliably on:**
- All mechanical conventions (sealed handlers, `ConfigureAwait(false)`, `Result<T>`, file placement)
- All structural patterns (idempotency guards, tenant-scoped queries, audit entries in the same `SaveChangesAsync`)
- All boilerplate (EF configuration, FluentValidation rules, OpenAPI metadata, DI registration)
- The integration between modules — once the Contracts were defined, wiring the event handlers was mechanical

**Humans reviewed carefully on:**
- Clinical role boundaries — whether a Pharmacist can or cannot perform a specific action is a domain decision the AI cannot infer from code
- Data model decisions — nullable columns, index choices, relationship cardinality
- PHI edge cases — indirect PHI (an appointment time + clinic name combined can identify a patient in a rural community) isn't caught by `phi-review`'s structural checks
- Every generated migration — the `create-migration` skill includes a review checklist, but correctness (not just form) requires human judgment

The architecture tests added in Part 9 confirm the boundaries stayed clean across all ten parts — not because every AI session was perfect, but because the structure made violations detectable and fixable before they compounded.

---

## Cross-module data flow: how events connect everything

The most interesting architectural question in a modular monolith isn't "how do I build a module" — it's "how do modules talk to each other without coupling?"

The answer in MediClinic is integration events via Mediator notifications:

```
Patient registers
    → Appointments validates patient exists (via PatientExistsQuery in Patients.Contracts)

Doctor books appointment
    → AppointmentBookedIntegrationEvent published
    → Notifications subscribes → checks consent → sends SMS reminder

Doctor closes encounter
    → EncounterClosedIntegrationEvent published
    → Billing subscribes → creates Draft invoice

Patient pays invoice
    → InvoicePaidIntegrationEvent published
    → Notifications subscribes → checks consent → sends payment confirmation
```

No module calls another module's handler directly. No module imports another module's runtime project. Every cross-module communication goes through a `.Contracts` event or query — a boundary that's checked by architecture tests on every build.

The complete event catalogue:

| Publisher | Event | Subscriber (what it does) |
|---|---|---|
| Appointments | `AppointmentBookedIntegrationEvent` | Notifications — consent-gated SMS reminder |
| Encounters | `EncounterClosedIntegrationEvent` | Billing — creates Draft invoice |
| Billing | `InvoicePaidIntegrationEvent` | Notifications — consent-gated payment confirmation |

---

## Multi-tenancy: the invisible foundation

Tenant isolation is the constraint that shapes every query in the system.

The mechanism: EF Core global query filters in `BaseDbContext`, applied automatically to every entity that inherits `AuditableEntity`. A developer who forgets to think about tenancy still gets a scoped query. The unsafe behavior — reading across tenants — requires actively fighting the infrastructure with `IgnoreQueryFilters()`, which is banned on clinical data by architecture rules.

The tenant identity flows from the JWT `clinic_id` claim. When a request arrives, Finbuckle reads the `X-Tenant-Id` header and resolves the tenant. A middleware validates that the JWT's `clinic_id` matches the header — preventing a token from one clinic being used to access another clinic's data.

What happens if that check is missing? The tenant isolation test added in Part 9 catches it:

```csharp
// Create a patient under clinic A
// Try to read that patient under clinic B
// Assert: the patient is invisible to clinic B

visibleToB.Should().BeNull(
    because: "the global query filter must prevent cross-tenant reads");
```

This test would fail if `base.OnModelCreating(modelBuilder)` was called in the wrong order, if the filter was accidentally removed, or if the `ITenantContext` was registered as a singleton that didn't change per request. These are exactly the bugs that pass code review and fail in production.

---

## The test pyramid for a modular monolith

Testing strategy evolved across the series. The final shape:

**Validator unit tests** — pure functions, no database, no infrastructure. The FluentValidation rules for each command are tested in complete isolation. Fast, deterministic, dozens of them.

**Handler integration tests** — real PostgreSQL via Testcontainers, no mocked `DbContext`. The handler is called with test doubles for `ITenantContext` and `IDbContextFactory<T>`. The database verifies that what was supposed to be written was actually written. Tenant isolation and audit trail tests live here.

**Architecture tests** — NetArchTest reflection-based checks that run in two seconds with zero I/O. They verify: no module runtime references another module runtime; all handlers are `sealed`; all handlers live in `Features/` namespaces.

The key insight from `testing.md`: **never mock `DbContext`.** Mocked EF Core doesn't execute the actual SQL, doesn't apply query filters, and doesn't catch migration drift. The three things most likely to cause a data breach in a multi-tenant system are exactly what mocked DbContexts can't detect.

---

## What each part builds

### Part 1 — Building Blocks
*The Shared Infrastructure Every Module Depends On*

`Result<T>`, `ITenantContext`, `AuditableEntity`, `BaseDbContext`, `ValidationFilter`. The boring foundation that makes everything else possible. Why `Result<T>` instead of exceptions. Why `TimeProvider` instead of `DateTime.UtcNow`. How `BaseDbContext` applies tenant isolation automatically.

**What you'll learn:** How to build shared infrastructure that enforces constraints by default, not by discipline.

---

### Part 2 — Patients Module
*Your First Vertical Slice with Full PHI Safety*

The reference module. Patient aggregate with private setters and behavior methods. The `RegisterPatient` vertical slice from Command → Handler → Validator → Endpoint. The `add-module` and `add-feature` skills in practice. The `phi-review` workflow catching a logging mistake.

**What you'll learn:** How to build a vertical slice that's correct, PHI-safe, and tenant-scoped by construction.

---

### Part 3 — Appointments Module
*State Machines, Cross-Module Queries, and Integration Events*

The Appointment aggregate with status transitions (`Pending → Confirmed → CheckedIn → Completed → Cancelled`). State machine methods that enforce invariants and return `Result`. The `PatientExistsQuery` cross-module pattern — querying across boundaries without runtime coupling. `AppointmentBookedIntegrationEvent` as the trigger for notifications.

**What you'll learn:** How to model aggregate state machines and how modules talk to each other without importing each other.

---

### Part 4 — Encounters Module
*Clinical Notes, ICD-10 Diagnoses, and the Audit Trail*

The Encounter entity as an owned-entity aggregate (Vitals, Diagnoses). The audit trail requirement in code: every encounter read and write emits an `AuditEntry` in the same `SaveChangesAsync` call — atomically. `EncounterClosedIntegrationEvent` as the trigger for Billing.

**What you'll learn:** How to implement a legally-required audit trail that's genuinely atomic, not best-effort.

---

### Part 5 — Prescriptions Module
*Drug Orders, Allergy Checks, and the Pharmacist Workflow*

The business rule that no prescription exists without a closed Encounter. Allergy conflict checking via cross-module query to `Patients.Contracts`. Why drug names are PHI: logging patterns that stay compliant. The dispense lifecycle: Draft → Active → Dispensed as aggregate transitions.

**What you'll learn:** How to enforce complex cross-module business rules without coupling modules at runtime.

---

### Part 6 — Identity and Security
*JWT Auth, Role-Based Permissions, and the Cross-Tenant Attack*

Three roles (Doctor, Pharmacist, Admin) with fine-grained permissions (`Encounters.Create`, `Prescriptions.Dispense`). JWT structure with `clinic_id` as the tenant security claim. `TenantClaimValidationMiddleware` that prevents a valid JWT from one clinic accessing another clinic's data. Rate limiting on auth endpoints.

**What you'll learn:** How to implement multi-tenant JWT authentication where the token itself carries the security boundary.

---

### Part 7 — Billing Module
*Event-Driven Invoices and the Draft → Issued → Paid Lifecycle*

Invoice creation triggered by `EncounterClosedIntegrationEvent` — why clinical closure is the correct billing trigger. Idempotency guard: one invoice per encounter, enforced by a unique index and a handler check. The Invoice state machine with voiding rules. `InvoicePaidIntegrationEvent` as the trigger for payment notifications.

**What you'll learn:** How to build an event-driven module that creates no HTTP endpoints and requires no manual triggering.

---

### Part 8 — Notifications Module
*Consent-Gated Reminders and PHI-Safe Error Logging*

A module with no endpoints — a pure event consumer. `GetPatientContactQuery` as a minimal cross-module query that returns only phone + consent flag, not name/DOB. The consent check as structure: the early return makes `INotificationSender` structurally unreachable without consent. Why `ex.GetType().Name` instead of `ex.Message` in the catch block (provider SDKs include the phone number in exception messages).

**What you'll learn:** How to design consent compliance as a structural guarantee rather than a code convention.

---

### Part 9 — Testing
*Architecture Tests, Testcontainers, and the Tenant Isolation Test*

NetArchTest module boundary enforcement — 15 tests in 2 seconds, no I/O. Testcontainers PostgreSQL integration tests with `EnsureCreated()` (not migrations) and fresh-GUID tenant isolation. The tenant isolation test that would catch a missing `base.OnModelCreating()` call. The audit trail test that verifies the `AuditEntry` row exists in PostgreSQL after `SaveChangesAsync`. Why the validator has its own unit tests separate from the handler.

**What you'll learn:** How to build a test suite that catches the specific failures that cause data breaches and audit gaps in a multi-tenant SaaS.

---

### Part 10 — The AI Development Layer
*AGENTS.md, Skills, Workflows, and Prompt Patterns*

How the `.agents/` directory is structured and why. The skills vs. workflows distinction (verbs vs. sequences). The `feature-scaffolder` workflow from task description to green build. `phi-review` and `architecture-guard` as mandatory gates. The prompt pattern that produces consistent AI output across Claude, Gemini, and Cursor. Honest lessons: what AI contributed reliably, where human review was non-negotiable.

**What you'll learn:** How to set up a codebase so that AI assistance produces correct, safe output — and how to verify that it has.

---

## How to follow along

Every article corresponds to a git tag. Check out any tag to see the exact codebase at that moment in the series:

```bash
git clone https://github.com/your-username/MediClinic
git checkout article/part-1    # BuildingBlocks complete
git checkout article/part-4    # Encounters module, audit trail
git checkout article/part-9    # Full system with tests
git checkout article/part-10   # Complete series
```

To run the full system:

```bash
# Start PostgreSQL (requires Docker)
docker run -e POSTGRES_PASSWORD=postgres -p 5433:5432 postgres:16-alpine

# Apply migrations
dotnet run --project src/Host/MedClinic.DbMigrator

# Start the API
dotnet run --project src/Host/MedClinic.Api

# Open Scalar API explorer
# https://localhost:7xxx/scalar
```

To run the architecture tests (no Docker required):

```bash
dotnet test tests/Architecture
# 15 tests, ~2 seconds, zero I/O
```

To run the integration tests (Docker required):

```bash
dotnet test tests/Integration
# Starts PostgreSQL container automatically via Testcontainers
```

---

## Who this series is for

**You're building a multi-tenant SaaS on .NET** and want to see how tenant isolation, audit trails, and role-based permissions work in a real codebase — not a "coming soon" section in a tutorial.

**You're working with AI coding assistants** (Claude Code, Cursor, Gemini CLI) and want to understand how to structure a repository so that AI contributions are reliable rather than risky.

**You're interested in modular monolith architecture** and want to see the module boundary enforcement, vertical slice organization, and cross-module event patterns applied to a domain that has real constraints.

**You've read the theory** on clean architecture, DDD, and domain events, and you want to see it implemented in a complete system where all the pieces work together.

---

## What makes this series different

Most .NET tutorials show you the happy path. This series shows you:

- The decisions that were almost made differently, and why they weren't
- The bug that the architecture test caught before it reached production (Part 9)
- The PHI violation the `phi-review` workflow found and how it was fixed (Part 4)
- The DI validation error that appeared at startup and exactly what caused it (Part 6 follow-up)
- The migration footgun that corrupts the EF snapshot and how the skill prevents it (Part 7)

Real systems don't have clean happy paths. They have constraints, tradeoffs, and mistakes that get caught and fixed. Showing those is the honest version of this work.

---

*The complete source code is on GitHub. Every commit corresponds to one article in the series. Start with Part 1, or jump to the part that covers the problem you're currently solving.*

*Each article ends with a link to the next. The series is designed to be read sequentially, but every part is self-contained enough to be useful on its own.*

---

**Next: [Part 1 — Building Blocks: The Shared Infrastructure Every Module Depends On](#)**
