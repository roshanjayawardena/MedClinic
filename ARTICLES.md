# MediClinic — Medium Article Series Roadmap

> **Series title:** *Building a Production SaaS with AI: A MediClinic Reference Implementation*
>
> Each article is paired with a git tag (`article/part-N`) so readers can check out the exact state
> of the code at any point in the series. Every article is also a tutorial: explain the "why" first,
> then the "how", then link to the exact commit diff.

---

## Series philosophy

- **Show the decisions, not just the code.** Every architectural choice has a tradeoff; name it.
- **The AI artifact layer is a first-class topic.** AGENTS.md, rules, skills, and workflows are
  content, not scaffolding. Readers should be able to copy and adapt them.
- **Small, complete commits.** Each article corresponds to one reviewable diff — not a "big-bang" dump.
- **Honest about the domain.** A private clinic has real data-privacy requirements. Show how to handle
  PHI, tenant isolation, and audit trails at the implementation level, not just in theory.

---

## Part 0 — The Blueprint (Published first, code optional)

**Title:** *How I'm Building a Production Medical Clinic SaaS — Architecture Decisions Before a Single Line of Code*

**What it covers:**
- Why a modular monolith, not microservices, for a small clinic
- The fullstackhero philosophy: copy-and-own, no black-box frameworks
- The domain: 1 doctor, 1 pharmacist, real PHI obligations
- How AI agents (Claude Code, Cursor, Gemini CLI) fit into the workflow
- Tour of AGENTS.md and the `.agents/` folder — what it is, why it matters
- The 7-module map and how each module knows its boundaries

**Git tag:** `article/part-0`

**Key artifacts to show:**
- `AGENTS.md` (full file)
- `.agents/rules/architecture.md`
- `.agents/rules/phi-and-tenancy.md`
- Repository structure diagram

---

## Part 1 — The Foundation

**Title:** *Building Blocks: The Shared Infrastructure Every Module Depends On*

**What it covers:**
- `Result<T>` — why we don't throw for business failures
- `ITenantContext` — how every query becomes tenant-scoped automatically
- `AuditableEntity` — audit stamps, soft-delete, and tenant key in one base class
- `BaseDbContext` — the global query filter that makes tenant isolation default-on
- `ValidationFilter` — why FluentValidation runs before the handler, not inside it

**Git tag:** `article/part-1`

**Key code to show:**
- `src/BuildingBlocks/Core/Result.cs`
- `src/BuildingBlocks/Core/ITenantContext.cs`
- `src/BuildingBlocks/Persistence/BaseDbContext.cs`

---

## Part 2 — The Patients Module (Reference Implementation)

**Title:** *Your First Vertical Slice: Registering a Patient with Full PHI Safety*

**What it covers:**
- Module structure: runtime + Contracts, why this boundary matters
- The `add-module` skill walkthrough (readers follow along)
- The Patient aggregate: private setters, behavior methods, consent enforcement
- `RegisterPatient` feature: Command → Handler → Validator → Endpoint
- Tenant-scoped queries: how Finbuckle + BaseDbContext handle isolation silently
- The `phi-review` workflow: what it checks and why

**Git tag:** `article/part-2`

**Key code to show:**
- `src/Modules/Patients/Patients/Domain/Patient.cs`
- `src/Modules/Patients/Patients/Features/RegisterPatient/`
- `src/Modules/Patients/Patients.Contracts/RegisterPatient.cs`
- `.agents/skills/add-feature/SKILL.md`

---

## Part 3 — The Appointments Module

**Title:** *State Machines in Practice: The Appointment Booking & Check-In Lifecycle*

**What it covers:**
- Aggregate state machines: why `CheckIn()` lives on the entity, not the handler
- `BookAppointment`: cross-module query (`PatientExistsQuery`) via Contracts
- The double-booking guard: business rule in the handler, not the DB
- Integration events: `AppointmentBooked` → Notifications subscriber
- The `add-integration-event` skill walkthrough

**Git tag:** `article/part-3`

---

## Part 4 — The Encounters Module

**Title:** *Handling PHI at the Code Level: Clinical Notes, Diagnoses, and the Audit Trail*

**What it covers:**
- Why every Encounter read/write emits an audit event (legal requirement, not nice-to-have)
- Owned entities in EF Core: `Diagnosis`, `VitalSigns` as value objects
- The `EncounterClosed` integration event and why Prescriptions depend on it
- The Outbox pattern: writing an audit event in the same transaction as the clinical change
- `phi-review` workflow catch: a "must fix" example and how it's fixed

**Git tag:** `article/part-4`

---

## Part 5 — The Prescriptions Module

**Title:** *The Pharmacist's Workflow: Drug Orders, Allergy Checks, and Dispensing*

**What it covers:**
- Business rule: no prescription without a closed Encounter
- Allergy conflict check via cross-module query (Patients.Contracts)
- Why drug names are PHI: logging patterns that stay compliant
- Dispense lifecycle: Draft → Active → Dispensed, aggregate transitions
- `PrescriptionDispensed` integration event → Billing

**Git tag:** `article/part-5`

---

## Part 6 — Identity & Security

**Title:** *JWT Auth, Role-Based Permissions, and Multi-Tenant Security in .NET 10*

**What it covers:**
- Role model: Doctor, Pharmacist, Receptionist, Admin
- Fine-grained permissions: `Encounters.Create`, `Prescriptions.Dispense`
- JWT token structure: `clinic_id` claim as the security boundary
- Rate limiting on auth endpoints
- `add-permission` skill walkthrough
- The `security.md` rules in practice

**Git tag:** `article/part-6`

---

## Part 7 — Billing

**Title:** *Simple Billing for a Private Clinic: Invoices Triggered by Clinical Events*

**What it covers:**
- Event-driven invoice creation: `AppointmentCompleted` → draft invoice
- Line items: consultation fee, procedure codes
- Invoice lifecycle: Draft → Issued → Paid → Void
- Why billing is its own module (not bolted onto Appointments or Encounters)

**Git tag:** `article/part-7`

---

## Part 8 — Notifications

**Title:** *Consent-Gated Reminders: Sending Appointment Notifications the Right Way*

**What it covers:**
- `ConsentToCommunications` check before any outreach
- Integration event subscriber: `AppointmentBooked` → schedule reminder
- The `Notifications` module as a pure consumer (no business logic)
- Email/SMS abstraction: swappable providers behind an interface

**Git tag:** `article/part-8`

---

## Part 9 — Testing the Whole Thing

**Title:** *How to Test a Multi-Tenant SaaS: Architecture Tests, Integration Tests, Testcontainers*

**What it covers:**
- NetArchTest: mechanically enforcing module boundary rules
- Testcontainers: real PostgreSQL, no mocked DbContext
- Tenant isolation test: create data in clinic A, verify clinic B cannot see it
- Testing the audit trail: command executes → audit event emitted
- The `testing.md` skill and what "done" means for a feature

**Git tag:** `article/part-9`

---

## Part 10 — The AI Development Layer

**Title:** *AI-Native SaaS: How AGENTS.md, Skills, and Workflows Accelerate Every Feature*

**What it covers:**
- AGENTS.md as the single source of truth for human and AI contributors
- Skills vs. workflows: verbs (repeatable tasks) vs. orchestration (sequenced review)
- The `feature-scaffolder` workflow: from task description to green build
- `architecture-guard` and `phi-review` as automated gates, not checklists
- Prompt patterns that work: how to ask Claude/Gemini/Cursor to follow a skill
- Lessons learned: what AI gets right, where humans still need to review

**Git tag:** `article/part-10`

---

## Git tag convention

After completing each article's code:
```bash
git tag article/part-N -m "Article Part N: <title>"
git push origin article/part-N
```

Readers can `git checkout article/part-N` to see the exact state of the codebase at any article.
