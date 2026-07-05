# Building a Production Medical Clinic SaaS — Architecture Decisions Before a Single Line of Code

*Part 0 of the MediClinic series: Building a Production SaaS with AI*

---

Most tutorial projects are dishonest.

They show you the happy path. A clean `ProductsController`, an `AppDbContext` with two tables, a `README` that says "clone and run." Then you try to build something real on top — add multi-tenancy, audit trails, role-based access, cross-module communication — and everything falls apart, because the foundation was never designed to carry that weight.

This series is different. We're building **MediClinic**: a production-grade, multi-tenant SaaS for private medical clinics. Real domain. Real compliance requirements. Real architectural constraints. And we're building it from scratch, in public, explaining every decision as we go.

This first article contains zero implementation code. Instead, it covers something most tutorial series skip entirely: **the decisions made before writing a single line of production code.** Why this architecture, not that one. Why this tech stack. What the domain actually demands. And — something you won't see often — how we're setting up the repository so that AI agents can contribute to it reliably, not just humans.

Let's start at the beginning.

---

## The domain: what MediClinic actually is

MediClinic is a clinic management system for a **single-doctor private practice.** One GP. One pharmacist (who also handles front-desk duties). A small, known patient population. Not a hospital system, not a health network — a small private clinic.

That scope is intentional. Small enough to build end-to-end in a realistic article series. Large enough to surface real engineering problems: PHI handling, multi-tenancy, audit trails, role boundaries, cross-module data flow.

The system needs to cover:

- **Patient registration** — demographics, consent, allergies
- **Appointment scheduling** — booking, check-in, check-out, cancellation
- **Clinical encounters** — the doctor's notes, diagnoses (ICD-10), vitals
- **Prescriptions** — the doctor writes them, the pharmacist dispenses them
- **Billing** — consultation fees, invoices, payment tracking
- **Notifications** — appointment reminders, consent-gated

And it needs to do all of this **multi-tenant**, so the same codebase can power many independent clinics — each completely isolated from every other.

### The PHI reality

Here's what most .NET tutorials never discuss: healthcare software handles **Protected Health Information (PHI).** Patient names, dates of birth, diagnoses, medications — these have legal protection in most jurisdictions. GDPR in Europe, HIPAA in the US, PDPA in Sri Lanka and Southeast Asia.

This isn't just a compliance checkbox. It shapes every architectural decision:

- Patient names must **never appear in log files**, even at debug level
- Every read or write of a clinical record must emit an **audit trail**
- Patient data belongs to exactly one clinic — **no cross-tenant reads, ever**
- Patients must give **explicit consent** before their data is processed

We'll see these constraints become concrete code in Part 2. For now, understanding them is what drives the architecture.

---

## Why a modular monolith, not microservices

The first question any architect asks about a new system: monolith or microservices?

For MediClinic, the answer is **modular monolith**, and here's why.

Microservices solve two problems: independent deployability and independent scalability. A private clinic system needs neither. The load profile is tiny — one doctor, one pharmacist, maybe a few dozen patient interactions a day. And independent deployability adds operational complexity (service discovery, network calls, distributed tracing, separate deployment pipelines) that a two-person clinic has no capacity to operate.

What we *do* need is **organizational clarity**. The Prescriptions feature shouldn't be able to accidentally reach into Patient demographics without going through a defined interface. The Billing module shouldn't share a database table with Appointments. The codebase should be navigable — "where does appointment booking live?" should have an obvious answer.

A modular monolith gives you that: one deployment unit, one database (multiple schemas), but clear boundaries enforced by the project structure. You get the simplicity of a monolith with the organizational discipline of separate services. And if the load profile ever demands it, the module boundaries make it possible to extract a service later — the seams are already there.

> **The tradeoff:** A modular monolith requires discipline about boundaries. Nothing in a monolith *prevents* you from calling across boundaries at runtime. In a microservices setup, the network is the enforcer. Here, architecture tests and code conventions are the enforcer — and we'll build both.

---

## Why fullstackhero as the foundation

We're building on top of the architectural patterns established by [fullstackhero](https://fullstackhero.net) — a production-first .NET 10 starter that [Mukesh Murugan](https://codewithmukesh.com/blog/introducing-fullstackhero/) has documented in detail.

The key idea that makes fullstackhero different from most starter kits: it's **copy-and-own, not a framework.** You get the full source code. If you don't like how multi-tenancy resolves, you open the file and change it. If the audit interceptor doesn't fit your domain, you rewrite it. There are no black-box NuGet packages whose internals you can't touch.

MediClinic adopts fullstackhero's architectural patterns — vertical slices, modular layout, the `.agents/` convention — but applies them to a real healthcare domain. Think of it as fullstackhero applied to a specific, non-trivial problem.

---

## The architecture: vertical slices inside modules

Every feature in MediClinic follows the same shape, regardless of which module it lives in:

```
A feature = one folder containing:
  ├── A Command or Query   (in the module's .Contracts project)
  ├── A Handler            (processes the command, owns the logic)
  ├── A Validator          (FluentValidation, runs before the handler)
  └── An Endpoint          (thin Minimal API, just maps HTTP to Mediator)
```

There are no service layers. No repository interfaces. No `IProductService` with a `ProductService` implementation. The handler talks directly to the module's `DbContext`. The endpoint talks to Mediator. That's it.

This is the **vertical slice** pattern. Instead of organizing code by layer (Controllers/, Services/, Repositories/), you organize by feature. Everything that touches `RegisterPatient` lives in `Features/RegisterPatient/`. You can delete the feature by deleting the folder.

Vertical slices feel strange if you're used to layered architecture, but they have a concrete benefit: **locality.** When you're working on a feature, everything you need is in one place. You don't have to jump between four layers to understand what `RegisterPatient` does.

---

## The module map

Seven modules, one deployed process:

```
src/Modules/
├── Patients/        — Demographics, consent, allergies
├── Appointments/    — Scheduling and check-in lifecycle
├── Encounters/      — Clinical notes, diagnoses, vitals
├── Prescriptions/   — Drug orders (doctor writes, pharmacist dispenses)
├── Identity/        — JWT auth, three roles
├── Billing/         — Invoices triggered by clinical events
└── Notifications/   — Reminder delivery (consent-gated)
```

Each module is two projects:

- **`<Module>/`** — the runtime project. Contains `Domain/`, `Persistence/`, `Features/`, and a module registration class. No other module may reference this project directly.
- **`<Module>.Contracts/`** — the public surface. Contains the Commands, Queries, Responses, and integration events that other modules are allowed to see.

The boundary rule is absolute: **a module may reference another module's `.Contracts` project, never its runtime project.** This is checked mechanically by architecture tests (NetArchTest) that run on every build.

What does cross-module communication look like when direct references are forbidden? You go through Mediator:

```csharp
// In the Appointments module — booking needs to verify the patient exists.
// Appointments cannot import Patients runtime. It sends a query via Mediator,
// which resolves to a handler in the Patients runtime.

var exists = await mediator.Send(new PatientExistsQuery(command.PatientId), ct);
if (!exists.Value) return Result<BookAppointmentResponse>.Fail("Patient not found.");
```

`PatientExistsQuery` lives in `Patients.Contracts`. The Appointments module references Contracts, never the Patients runtime. The boundary holds.

---

## The three roles

In this particular clinic, two people use the software:

| Role | Who | What they do |
|---|---|---|
| Doctor | The GP | Creates encounters, writes prescriptions, owns clinical decisions |
| Pharmacist | The pharmacist (+ front desk) | Reads prescriptions, dispenses medicine, books appointments |
| Admin | Clinic owner (typically the doctor) | Manages settings, users, billing |

There's no separate Receptionist role — the pharmacist handles front-desk duties too. This simplicity is real-world; in a one-or-two-person practice, roles aren't cleanly separated by headcount.

Permissions are fine-grained: `Encounters.Create` belongs to the Doctor role. `Prescriptions.Dispense` belongs to Pharmacist. The same prescription object is involved, but the two roles touch different parts of its lifecycle.

---

## The tech stack and why

| Layer | Choice | Why |
|---|---|---|
| Runtime | .NET 10 | LTS, performance, minimal allocations |
| API | Minimal APIs | No controller boilerplate; endpoints stay thin by design |
| Mediator | Source-generated Mediator | Compile-time dispatch, zero reflection at runtime |
| Validation | FluentValidation 11 | Expressive rules, DI-friendly, testable in isolation |
| ORM | EF Core 10 | Global query filters = tenant isolation by default |
| Database | PostgreSQL 16 | Production-grade open source; strong .NET ecosystem |
| Multi-tenancy | Finbuckle.MultiTenant | Purpose-built for .NET; header-based resolution |
| Auth | ASP.NET Core Identity + JWT | Standard, well-understood, avoids vendor lock-in |
| Logging | Serilog | Structured output; PHI-safe template discipline |
| API docs | Scalar | Modern replacement for Swagger UI |
| Testing | xUnit + Testcontainers + NetArchTest | Real DB for integration tests; no mocked DbContexts |

One call-out: **source-generated Mediator, not MediatR.** The source generator creates the dispatch code at compile time, so there's no runtime reflection scanning assemblies. The API looks similar to MediatR, but it's not — and the handler shape is slightly different. We'll cover this in Part 1.

---

## Multi-tenancy: the invisible foundation

Multi-tenancy is the most architectural concern that's easy to retrofit badly. Every query that touches patient data needs to be scoped to the current clinic. Miss it once, and clinic A can see clinic B's patients — a catastrophic data leak.

The approach: **global query filters in EF Core, applied by default.** Every module's `DbContext` inherits from a `BaseDbContext` that automatically applies a tenant filter to every entity. You never write `WHERE clinic_id = @clinicId` in your queries — the filter is always on. The only way to remove it is `IgnoreQueryFilters()`, which is banned on clinical data by architecture rules.

Finbuckle resolves the tenant from the JWT's `clinic_id` claim. The resolved tenant flows into an `ITenantContext` that the `DbContext` depends on. From that point, tenant isolation is the infrastructure's job, not the developer's.

This approach has one notable property: **it defaults to safe.** A developer who forgets to think about tenancy still gets a scoped query. The unsafe behavior — reading across tenants — requires actively fighting the infrastructure.

---

## The AI development layer

Here's something you don't see in most project repos: `AGENTS.md`.

```
MediClinic/
├── AGENTS.md           ← the canonical guide for AI agents
├── CLAUDE.md           ← Claude Code bridge (imports AGENTS.md)
└── .agents/
    ├── rules/          ← convention files, loaded on demand
    ├── skills/         ← task recipes (add-feature, add-module, create-migration…)
    └── workflows/      ← orchestration and review playbooks
```

This repository is designed to be developed with AI assistance — Claude Code, Cursor, Gemini CLI, or whatever comes next. The `.agents/` directory is the interface between human intent and AI execution.

**Why does this matter?** Without it, an AI coding agent working on a new feature has to infer conventions from the existing code. It may get them right, or it may introduce a handler that doesn't propagate `CancellationToken`, an endpoint that contains business logic, or a query that bypasses the tenant filter. With explicit rules and skills, the agent has a specification to follow.

The structure works like this:

**Rules** capture non-obvious conventions — things a skilled developer would know but a new contributor (human or AI) might not:

```
.agents/rules/
├── architecture.md        — module layout, boundaries, file placement
├── api-conventions.md     — handler shape, endpoint style, Result<T> usage
├── database.md            — EF Core patterns, migrations, tenant isolation
├── phi-and-tenancy.md     — PHI handling, logging prohibitions
├── auditing.md            — clinical record audit trail requirements
├── eventing.md            — domain events vs. integration events
├── security.md            — roles, permissions, JWT, rate limiting
├── logging.md             — structured templates, PHI prohibition
├── testing.md             — Testcontainers, no mocked DbContexts
└── modules/               — per-module quirks and domain rules
    ├── patients.md
    ├── appointments.md
    ├── encounters.md
    └── prescriptions.md
```

**Skills** are task recipes — step-by-step instructions for repeatable tasks:

```
.agents/skills/
├── add-feature/       — new command/query/handler/validator/endpoint
├── add-entity/        — new domain aggregate with EF configuration
├── add-module/        — new bounded context, end to end
├── create-migration/  — EF Core migration, footgun-aware
├── add-integration-event/  — cross-module event via Outbox
└── add-permission/    — new role boundary or permission constant
```

**Workflows** are orchestration playbooks — they sequence skills and add review gates:

```
.agents/workflows/
├── feature-scaffolder   — entity → feature → migration → tests → review
├── module-creator       — new module, all wiring sites, smoke test
├── architecture-guard   — boundary validation (read-only, no code changes)
├── code-reviewer        — diff review against conventions
└── phi-review           — PHI and audit compliance check
```

When you ask Claude Code "add the BookAppointment feature to the Appointments module," it reads AGENTS.md, loads `api-conventions.md` and `database.md` from rules, follows the `add-feature` skill step by step, then runs `code-reviewer` and `phi-review` to self-check before declaring done.

The result: consistent, convention-following code whether the contributor is a human developer or an AI agent.

### Why AGENTS.md instead of just CLAUDE.md?

`CLAUDE.md` is Claude Code's entry point. `GEMINI.md` is Gemini CLI's. `.cursorrules` is Cursor's. If the project's conventions only lived in `CLAUDE.md`, they'd be tool-specific and duplicated.

`AGENTS.md` is **tool-neutral** — it's the canonical specification, and each tool-specific file is a one-line import:

```markdown
# CLAUDE.md
The canonical project guide is AGENTS.md (tool-neutral). It is imported below.
@AGENTS.md
```

One source of truth, consumed by any AI tool. When a convention changes, you change it once.

---

## The 10 golden rules

Every decision in this codebase flows from ten non-negotiable rules. These are documented in `AGENTS.md` so any contributor — human or AI — reads them first:

```
1. Return Result<T> from handlers; never throw for expected failures.
2. Inject the module's own DbContext; no repository wrappers.
3. Propagate CancellationToken through every async call.
4. TimeProvider.GetUtcNow() — never DateTime.Now directly.
5. Minimal API endpoints are thin; all logic lives in the handler.
6. sealed handlers, file-scoped namespaces, primary constructors.
7. Every patient/clinical query MUST be tenant-scoped.
8. NEVER log PHI (names, DOB, contact info, diagnoses) in plaintext.
9. Every read or write of a clinical record MUST emit an audit entry.
10. Modules reference only each other's .Contracts — never the runtime project.
```

Each rule exists for a specific reason. Rule 1 (Result<T>) avoids exception-based control flow that's hard to test and obscures intent. Rule 8 (no PHI in logs) is a legal requirement, not a style preference. Rule 10 (Contracts only) is what keeps the module monolith from becoming a tightly coupled ball of mud.

By Part 10 of this series, we'll have touched every one of these rules in production code — and you'll see exactly what happens when one is violated.

---

## The repository structure, in full

```
MedClinic/
├── src/
│   ├── BuildingBlocks/
│   │   ├── Core/          — Result<T>, ITenantContext, IModule, AuditEntry
│   │   ├── Persistence/   — BaseDbContext (tenant filter + soft-delete)
│   │   └── Web/           — ValidationFilter, host wiring
│   ├── Host/
│   │   ├── MedClinic.Api/                    — Composition root
│   │   ├── MedClinic.DbMigrator/             — Deploy-time migration runner
│   │   └── MedClinic.Migrations.PostgreSQL/  — All EF migrations
│   └── Modules/
│       ├── Patients/
│       │   ├── Patients/           — Runtime project
│       │   └── Patients.Contracts/ — Public surface
│       └── Appointments/, Encounters/, Prescriptions/, …
├── tests/
│   ├── Architecture/    — NetArchTest boundary enforcement
│   └── Integration/     — Testcontainers per module
├── .agents/
│   ├── rules/
│   ├── skills/
│   └── workflows/
├── AGENTS.md
├── CLAUDE.md
└── ARTICLES.md
```

One detail worth noting: **`MedClinic.DbMigrator` is a separate console app.** The API never runs migrations at startup — that's a common anti-pattern that causes race conditions in multi-instance deployments and makes rollbacks painful. Migrations run at deploy time, by the migrator, before the API starts.

---

## What's coming in the series

| Part | Topic |
|---|---|
| **Part 0** | Architecture decisions (this article) |
| **Part 1** | Building Blocks — `Result<T>`, `BaseDbContext`, `ValidationFilter` |
| **Part 2** | Patients module — first vertical slice, PHI safety |
| **Part 3** | Appointments — state machines, cross-module queries |
| **Part 4** | Encounters — PHI at the code level, audit trail |
| **Part 5** | Prescriptions — allergy checks, pharmacist workflow |
| **Part 6** | Identity — JWT, roles, fine-grained permissions |
| **Part 7** | Billing — invoice creation via integration events |
| **Part 8** | Notifications — consent-gated reminder delivery |
| **Part 9** | Testing — architecture tests, Testcontainers, tenant isolation |
| **Part 10** | AI-native development — AGENTS.md, skills, workflows in practice |

Each article pairs with a git tag (`article/part-N`) — check out any tag to see the codebase at that exact moment in the series.

---

## Before we write the first line of code

The decisions in this article — modular monolith over microservices, vertical slices over layered architecture, `BaseDbContext` global query filters for tenant isolation, `AGENTS.md` as the AI interface — aren't arbitrary preferences. Each one is a response to a specific constraint:

- Small team → simplicity over distributed system complexity
- Real domain → PHI-safe by default, not by discipline
- AI-assisted development → explicit conventions, not inferred ones
- Publishable series → every decision explained, every tradeoff named

The best architecture for a system is the simplest one that handles the real constraints. Not the most impressive one on a conference slide.

In Part 1, we build the foundation that every module depends on: `Result<T>`, `BaseDbContext`, `IModule`, and `ValidationFilter`. The boring infrastructure that makes everything else possible.

---

*The full source code is available at [github.com/your-username/MediClinic](https://github.com). Each commit corresponds to one article in the series.*

*Next: [Part 1 — Building Blocks: The Shared Infrastructure Every Module Depends On](#)*
