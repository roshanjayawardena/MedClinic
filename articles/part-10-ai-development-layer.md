# Part 10: AI-Native SaaS — How AGENTS.md, Skills, and Workflows Accelerate Every Feature

*Building a MediClinic SaaS — Part 10 of an ongoing series*

---

Nine parts in, we have a working multi-tenant clinic SaaS: seven modules, domain events, JWT auth, consent-gated notifications, and a test suite that enforces its own boundaries. Every part of this series was built with an AI assistant — Claude for most of it, with verification passes by Cursor and Gemini.

This part is different. Instead of a new module, it documents the *layer that makes AI assistance reliable*: the `.agents/` directory. This is the system that let an AI build Part 4 (Encounters) without accidentally logging a patient name, or build Part 7 (Billing) without adding a runtime dependency between modules.

The problem this layer solves is simple to state and hard to solve: AI assistants are extremely capable at writing code that *looks right* but misses non-obvious invariants. In a medical SaaS, "looks right but misses the tenant filter" is a data breach. "Looks right but logs the patient's phone number" is a PHI violation. The AI doesn't know what it doesn't know — and without structure, neither does the developer reviewing its output.

The `.agents/` directory is structured knowledge for an AI contributor.

---

## The problem with ad-hoc AI prompting

Here's a prompt that produces plausible but dangerous code for this codebase:

> "Add a handler that returns all encounters for a patient."

An AI given only this prompt will write a handler that does something like:

```csharp
var encounters = await db.Encounters
    .Where(e => e.PatientId == patientId)
    .ToListAsync(ct);
```

This query is wrong in three ways:
1. It doesn't filter by tenant — clinic B could read clinic A's encounters if the tenant filter isn't in the DbContext query filter
2. It doesn't emit an audit entry — reading an Encounter is a clinical access event that must be logged
3. It doesn't check permissions — any authenticated user can call it

An AI that has read `phi-and-tenancy.md`, `auditing.md`, and `api-conventions.md` before writing this handler will get all three right automatically. The files are not magic — they're just context the AI would have asked a senior developer for anyway.

The `.agents/` directory is that senior developer, codified.

---

## AGENTS.md: the single source of truth

`AGENTS.md` (with `CLAUDE.md` and `GEMINI.md` as thin bridges) is the file every AI reads first. It answers four questions an AI contributor needs before touching any code:

1. **What is this system?** — a private medical clinic SaaS, single-doctor practice, multi-tenant, not a hospital management system. Scope is deliberately small.
2. **What are the non-negotiable rules?** — ten golden rules, stated plainly, ordered by consequence.
3. **Where does code live?** — the repository map, down to which folder holds what type of file.
4. **How should I work here?** — the task type → skill mapping, telling the AI where to look next.

The ten golden rules are worth quoting in full because they're where the most common AI mistakes are prevented:

```
1. Return Result<T> from handlers; never throw for expected failures.
2. Inject the module's own DbContext; no repository wrappers.
3. Propagate CancellationToken through every async call.
4. TimeProvider.GetUtcNow() — never DateTime.Now or DateTime.UtcNow directly.
5. Minimal API endpoints are thin; all logic lives in the Mediator handler.
6. sealed handlers, file-scoped namespaces, primary constructors throughout.
7. Every patient/clinical query MUST be tenant-scoped. No cross-clinic reads.
8. NEVER log PHI (names, DOB, contact info, diagnoses) in plaintext.
9. Every read or write of an Encounter or Prescription MUST emit an audit entry.
10. Modules reference only each other's .Contracts — never the runtime project.
```

Rules 1–6 are mechanical conventions. An AI can follow them by imitation once it's seen an example handler. Rules 7–10 are safety invariants — they're harder because they require understanding the *reason*, not just the pattern. `AGENTS.md` provides both.

The domain rules section explains *why* each constraint exists:

| Rule | Why |
|---|---|
| Consent required at patient registration | Legal requirement; no consent = no record |
| Soft-delete only for patient/clinical data | Audit trail and regulatory retention |
| Prescriptions need an active Encounter | A script without a consult is a data integrity violation |
| Pharmacist can view/dispense; cannot create Encounters | Role boundary: clinical vs. pharmacy |

An AI that understands *why* a rule exists will apply it correctly in novel situations. An AI that only knows the rule as a pattern will apply it in the obvious case and miss the edge case.

---

## Skills: repeatable tasks with exact steps

`.agents/skills/` holds six task recipes:

| Skill | When to use |
|---|---|
| `add-module` | New bounded context — an entire new module |
| `add-entity` | New domain aggregate or entity with EF config |
| `add-feature` | New vertical slice (command/query/handler/endpoint) |
| `add-integration-event` | Cross-module event with publisher and subscriber |
| `add-permission` | New role boundary or permission constant |
| `create-migration` | EF Core migration after an entity change |

The canonical example is `add-feature`. Its full text is six numbered steps, each with a verification gate. Step 2 (the handler) specifies:

> Handler: `public sealed`, returns `ValueTask<Result<T>>`, injects the module DbContext, `.ConfigureAwait(false)`, propagates the `CancellationToken`, stays tenant-scoped, calls aggregate methods for state changes.

This is everything a handler needs to be correct in this codebase. It's not written as a suggestion — it's a recipe. An AI following the skill exactly produces a handler that passes the architecture tests.

The most important part of any skill is the **gate**:

> **Verify:** `dotnet build`; `dotnet test`; if patient/clinical data was touched, the `phi-review` workflow. The slice is done only when the build is clean and tests are green.

Gates prevent partial completion. Without explicit "you are not done until X is true" language, an AI (and a human) will report the task complete when the happy path works, missing the failing edge case or the missing validator.

### The create-migration skill is a footgun catalogue

The migration skill deserves special attention because `dotnet ef migrations add` has multiple failure modes that are silent until the database is in a bad state:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/Host/MedClinic.Migrations.PostgreSQL \
  --startup-project src/Host/MedClinic.Migrations.PostgreSQL \
  --context <Module>DbContext \                 # wrong context = corrupted snapshot
  --output-dir Migrations/<Module>              # wrong dir = migration in wrong folder
```

The skill documents all four flags with an explanation of what each controls and what goes wrong if it's omitted. It also includes a review checklist:

```
Before applying, confirm:
[ ] Only the expected tables/columns are created or altered
[ ] No unexpected DROP statements
[ ] TenantId column is present on every tenant-scoped table
[ ] __EFMigrationsHistory uses the correct schema
```

An AI that has this checklist stops at the generated migration file and reviews it before running `dotnet run --project DbMigrator`. An AI without it runs the migrator immediately and discovers the problem in production.

---

## Workflows: orchestration with review gates

Where skills are recipes for a single task, workflows are orchestrations across multiple tasks with human-review points between them.

`.agents/workflows/` has five workflows:

| Workflow | Purpose |
|---|---|
| `feature-scaffolder` | Full slice from scratch: entity + feature + migration + tests |
| `module-creator` | New bounded context end-to-end |
| `code-reviewer` | Read-only diff review against conventions |
| `phi-review` | Read-only PHI and tenant safety gate |
| `architecture-guard` | Read-only module boundary compliance report |

The distinction between skills and workflows is conceptual: a skill is a verb (`add-feature`), a workflow is a sequence (`feature-scaffolder` calls `add-entity`, then `add-feature`, then `create-migration`, then tests, then reviews).

### feature-scaffolder: the main delivery workflow

`feature-scaffolder` exists for the common case: a new feature that requires a new entity. Its six steps are sequenced deliberately:

```
Step 0 — Identify context (which module, is entity new, does it touch clinical data?)
Step 1 — add-entity skill (with build gate)
Step 2 — add-feature skill (with build gate)
Step 3 — create-migration skill (with review gate on the SQL)
Step 4a — add-integration-event (if cross-module)
Step 4b — add-permission (if new role boundary)
Step 5 — Integration tests
Step 6 — code-reviewer + phi-review (mandatory)
```

Step 0 is the most important. Before writing a single line of code, the workflow asks five questions:

1. Which module owns this feature?
2. Does the entity already exist?
3. Does the feature touch clinical data?
4. Does completing the feature require publishing an integration event?
5. Does the feature require a new permission?

These questions determine which additional rules files are loaded (steps 3 and 4 in `AGENTS.md`'s "How to work here" section). Clinical data → load `phi-and-tenancy.md` and `auditing.md`. Cross-module event → load `eventing.md`. New permission → load `security.md`.

The order matters. Building the entity before the feature means the handler always has a correct domain model to call methods on. Building the feature before the migration means the migration is generated from a complete, buildable model — not from a half-written context.

### architecture-guard: the module boundary report

`architecture-guard` is a read-only workflow that inspects `.csproj` files and generates a compliance report. Its output looks like:

```
## Architecture Guard Report — 2026-07-09

### VIOLATIONS (must fix before merge)
- [critical] Appointments/Appointments.csproj references Patients runtime project (line 12)
- [critical] Encounters/Features/Handlers/ flat folder detected — use per-feature folders

### WARNINGS (should fix)
- [warn] GetAppointmentsHandler missing .ConfigureAwait(false) on line 34

### PASSED
- Module isolation: Patients, Prescriptions, Identity, Billing ✓
- Contracts purity: all .Contracts projects ✓

Verdict: VIOLATIONS FOUND — do not merge until critical items are resolved.
```

This report is deterministic — the same codebase always produces the same report. That means it can run after every module addition as a smoke test without requiring human interpretation. The checks it performs (csproj `<ProjectReference>` scanning, feature folder layout, handler shape sampling) are exactly what an experienced architect would do in a code review, automated.

### phi-review: the patient data safety gate

`phi-review` runs on any diff that touches patient, appointment, encounter, or prescription code. It checks five things:

```
- Tenant scoping: every query over clinical data scoped to the current clinic?
- PHI in logs: any name, DOB, contact, address, diagnosis, or medication in a log message?
- Consent: does any outreach path check ConsentToCommunications?
- Audit: does every read or write of an Encounter or Prescription emit an audit event?
- Deletion: is clinical data soft-deleted, never hard-deleted?
```

It reports by file and line number and gives a one-line verdict (`PHI-safe` / `not PHI-safe`). The format matters: a verdict the developer can see immediately without reading the full report makes this gate fast to act on.

The key insight behind `phi-review` is that PHI violations are structural, not accidental. A developer who logs `$"Patient {patient.Name} registered"` isn't being careless — they're using the most natural C# string construction. The phi-review gate makes the structural constraint explicit: use structured logging templates with surrogate IDs, never string interpolation with PHI fields.

---

## Rules files: on-demand context loading

`.agents/rules/` has fourteen files. They're not all loaded for every task — they're loaded on demand based on what the task touches.

```
architecture.md      ← any file placement question
api-conventions.md   ← adding an endpoint
database.md          ← entity, query, or migration
phi-and-tenancy.md   ← anything touching patient/clinical data
auditing.md          ← Encounters, Prescriptions, clinical record access
eventing.md          ← cross-module integration events
security.md          ← auth, permissions, rate limiting
logging.md           ← adding log statements
testing.md           ← writing tests
buildingblocks-protection.md ← before touching BuildingBlocks
modules/patients.md  ← Patients module specifics
modules/appointments.md
modules/encounters.md
modules/prescriptions.md
```

Loading only the relevant rules files keeps the AI's context focused. An AI working on a Billing feature doesn't need to read the Prescriptions module conventions. An AI adding a new permission doesn't need to read the database rules. Smaller context = less chance of the AI applying the wrong rule to the wrong situation.

The module-specific files (e.g., `modules/encounters.md`) document state machine transitions, invariants, and ownership rules that aren't obvious from the code:

> *The Encounter entity uses a status machine: `Open → Closed`. Only a Doctor can create an Encounter. A Pharmacist can read Encounters to check for a valid prescription context but cannot modify them. Closing an Encounter publishes `EncounterClosedIntegrationEvent` which triggers invoice creation in Billing.*

Without this context, an AI might let a Pharmacist create an Encounter (a role boundary violation), or forget to publish the event when closing (a data integrity violation).

---

## Prompt patterns that work

The difference between a productive AI session and an hour of back-and-forth is how the initial request is framed.

### The ineffective prompt

> "Add a feature to cancel an appointment."

This gives the AI a task but no context about conventions, constraints, or what "done" means.

### The effective prompt

> "Add a feature to cancel an appointment. Follow the `add-feature` skill in `.agents/skills/`. The appointment is in the Appointments module. Load `api-conventions.md`, `database.md`, and `modules/appointments.md` before writing any code. Done means: `dotnet build` clean, `dotnet test` green, `code-reviewer` workflow passes."

This gives the AI:
- The task (cancel an appointment)
- Where to find the conventions (the skill path)
- Which module it's in (so the right DbContext is used)
- Which rules files to load (no guessing)
- A definition of done (explicit verification steps)

### The pattern

```
[Task description] in the [Module name] module.
Follow the [skill name] skill in `.agents/skills/`.
Load [relevant rule files] before writing any code.
Done means: dotnet build clean, dotnet test green, [applicable review workflows] pass.
```

The "done means" clause is the most important part. Without it, an AI will report completion when the happy path compiles. With it, the AI knows it needs to run the build, run the tests, and run the review workflows before reporting back.

### Prompting for a workflow (not a skill)

When the task is larger — a full feature from entity to migration to tests:

> "Build the appointment cancellation feature end-to-end. Follow the `feature-scaffolder` workflow. Answer Step 0's five questions first, then proceed step by step. Show me the SQL in the generated migration before you run the migrator."

The "show me before you run" instruction is important for irreversible steps. The workflow already specifies reviewing the migration SQL, but reinforcing it in the prompt means the AI pauses at that specific gate rather than continuing automatically.

---

## What the `.agents/` directory doesn't solve

Being honest about limitations matters as much as the capabilities.

### What AI gets right

- **Mechanical conventions** — sealed handlers, `ConfigureAwait(false)`, `Result<T>` return types, file placement. Once the AI has seen one correct example plus a skill file, it follows the pattern reliably.
- **Structural patterns** — idempotency guards, tenant-scoped queries, audit entries in the same `SaveChangesAsync` call. The `add-feature` skill specifies these explicitly and the AI applies them consistently.
- **Boilerplate** — EF configuration, FluentValidation rules for standard types (NotEmpty, MaxLength, date validation), OpenAPI metadata, DI registration. This is where AI provides the most leverage: the cognitive cost of writing correct boilerplate is high for humans, zero for AI.

### Where humans still need to review

- **Novel business logic** — the AI doesn't know that a pharmacist can view but not create Encounters because of clinical role boundaries. The module rules file tells it, but if the rules file is wrong, the AI amplifies the error. A human must verify that role boundaries reflect actual clinical requirements.
- **PHI in novel forms** — the `phi-review` workflow checks for names and DOBs in log messages, but it can't reason about indirect PHI (e.g., a log that includes `appointmentTime` + `clinicName` might be enough to identify a patient in a small rural community). That judgment requires domain knowledge.
- **Data model decisions** — whether a field should be nullable, whether a relationship should be soft-deleted, whether an event needs a `CorrelationId`. These aren't in the rules files because they depend on context the files don't have. The AI will make a plausible choice; a human needs to verify it's the correct one.
- **Migration review** — the `create-migration` skill includes a review checklist, but the checklist checks *form* (is `TenantId` present, is the schema right) not *correctness* (is this the right index for the query patterns we'll use). Migration review is one place where human expertise is non-negotiable.

### The compounding value

The `.agents/` directory provides most value when it compounds over multiple AI sessions. Each session starts fresh — the AI has no memory of the previous session's decisions. Without `AGENTS.md`, every session requires re-establishing context: which Mediator library, what the `Result<T>` type is, why tenant filtering matters, how migrations work in this repo.

With `AGENTS.md`, a new session that starts by reading `AGENTS.md` has 90% of the context it needs in one file. The remaining 10% is loaded from the relevant skill and rules files based on what the task touches. A session that would have required 20 back-and-forth exchanges to establish context now requires two.

---

## The design philosophy

The `.agents/` directory rests on three principles:

**1. Constraints before capabilities.**  
The golden rules (especially 7–10) appear in `AGENTS.md` before any explanation of how to build features. An AI that understands the constraints first will follow them when the skills don't explicitly mention them. An AI that learns the constraints after the mechanics treats them as optional additions.

**2. Skills are verbs, workflows are sequences.**  
A skill answer the question "how do I do this one thing correctly?" A workflow answers "in what order do I do these things, and where do I stop and check?" Conflating the two produces bloated skills and workflows that have no natural stopping points.

**3. Review gates are structural, not procedural.**  
The `phi-review` and `code-reviewer` workflows are called explicitly in every workflow and skill. They're not a step that can be skipped by not reading the instructions — they appear in the completion checklist. A workflow that doesn't reach the review gates isn't complete.

---

## Practical next steps

To apply this pattern to your own project:

**Start with AGENTS.md.** Describe your system, its scope, its tech stack, and its non-negotiable rules. The non-negotiable rules are the most important part — these are where AI makes the most expensive mistakes.

**Write one skill before you need it.** The `add-feature` skill is the highest-leverage one for most web API projects. Define what a correct feature looks like — the complete list of files it creates, the properties each file must have, the verification steps. Then use it in your next AI session and refine based on what was unclear.

**Add phi-review last, if at all.** If your project doesn't touch health data or personal data, you don't need `phi-review`. The equivalent for your domain might be a security review (auth bypass, SQL injection), a performance review (N+1 queries, missing indexes), or a consistency review (event sourcing invariants). The *format* of the workflow (read-only, structured report, must-fix vs. should-fix, one-line verdict) transfers; the checks don't.

**Don't over-specify.** The rules files in this series are one to two pages each. Longer rules files increase the chance that the AI applies the wrong rule in the wrong context. One clear rule is better than three ambiguous ones.

---

## What we built across the series

This is the complete artifact list across ten parts:

| Part | Module | Key technical decision |
|---|---|---|
| 0 | Blueprint | Modular monolith over microservices — one deploy, seven modules |
| 1 | BuildingBlocks | `BaseDbContext` tenant filter applied automatically to every `AuditableEntity` |
| 2 | Patients | `Result<T>` as the handler return type; consent as a domain invariant |
| 3 | Appointments | State machine via aggregate methods; `CheckIn`, `Cancel`, `Complete` with guards |
| 4 | Encounters | Audit entry in the same `SaveChangesAsync` call as the entity write — atomic |
| 5 | Prescriptions | Allergy check before dispensing; drug name never appears in logs or events |
| 6 | Identity | JWT with `clinic_id` claim; `TenantClaimValidationMiddleware` blocks cross-tenant tokens |
| 7 | Billing | `EncounterClosed` → Draft invoice; idempotency guard; `LineTotal` computed in C# |
| 8 | Notifications | Consent as structure (early return); `INotificationSender` abstraction; PHI-safe error logging |
| 9 | Testing | Architecture tests (NetArchTest) + Testcontainers; fresh-GUID tenant isolation |
| 10 | AI Layer | AGENTS.md, skills, workflows — structured knowledge for AI contributors |

The system now has everything needed for a production-ready private clinic SaaS: complete patient-to-payment flow, tenant isolation at every layer, PHI protection throughout, role-based access control, an architecture that enforces its own rules at build time, and an AI development layer that makes adding the next feature as reliable as adding the last one.
