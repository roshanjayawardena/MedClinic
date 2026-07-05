# MediClinic — AI Contributor Guide

> **Tool-neutral canonical guide.** `CLAUDE.md`, `GEMINI.md`, `.cursorrules` all import this.
> Edit conventions here, not in the tool-specific bridges.

---

## What this system is

A **private medical clinic SaaS** for a single-doctor practice: one GP and one pharmacist serving a
small patient population. Multi-tenant so the same codebase can power many independent clinics, each
fully isolated from every other.

This is **not** a hospital management system. Scope is deliberately small:
appointments, clinical encounters, prescriptions, basic billing. Simplicity is a feature.

It is also a **reference implementation** for AI-native SaaS development, published as a Medium article
series. Every module, every architectural decision, and every AI artifact is written to be readable,
explainable, and reproducible by any developer following along.

---

## Repository map

```
MedClinic/
├── src/
│   ├── BuildingBlocks/              # Shared framework — Core, Persistence, Web
│   │   ├── Core/                   # Result<T>, ITenantContext, AuditEntry, AuditableEntity
│   │   ├── Persistence/            # BaseDbContext (tenant filter, soft-delete, audit stamps)
│   │   └── Web/                    # ValidationFilter, host wiring helpers
│   ├── Host/
│   │   ├── MedClinic.Api/          # Composition root — registers all modules
│   │   ├── MedClinic.DbMigrator/   # Runs migrations at deploy time, never at API startup
│   │   └── MedClinic.Migrations.PostgreSQL/  # All EF migrations, per-module subfolders
│   └── Modules/
│       ├── Patients/               # Demographics, consent, allergies (REFERENCE MODULE)
│       ├── Appointments/           # Scheduling, status lifecycle, reminders
│       ├── Encounters/             # Clinical notes, diagnoses (ICD-10), vitals
│       ├── Prescriptions/          # Drug orders — pharmacist workflow
│       ├── Identity/               # JWT auth, roles (Doctor/Pharmacist/Receptionist/Admin)
│       ├── Billing/                # Consultation fees, invoices, payment status
│       └── Notifications/          # Appointment reminders, outreach (consent-gated)
├── tests/
│   ├── Architecture/               # NetArchTest boundary enforcement
│   └── Integration/                # Testcontainers — each module in isolation
├── .agents/
│   ├── rules/                      # Convention files — loaded on demand
│   ├── skills/                     # Repeatable task recipes (verb: add-feature, add-entity…)
│   └── workflows/                  # Orchestration + review playbooks
├── AGENTS.md                       # ← you are here
├── CLAUDE.md                       # Claude Code bridge (@AGENTS.md import)
└── ARTICLES.md                     # Medium article series roadmap
```

---

## Tech stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10 |
| API style | Minimal APIs |
| Mediator | Source-generated **Mediator** (NOT MediatR — no IRequest<T> interface) |
| Validation | FluentValidation 11 |
| ORM | EF Core 10 |
| Database | PostgreSQL 16 |
| Multi-tenancy | Finbuckle.MultiTenant (header-based resolution) |
| Auth | ASP.NET Core Identity + custom JWT |
| Logging | Serilog + structured templates |
| API docs | Scalar (replaces Swagger UI) |
| Testing | xUnit + Testcontainers + NetArchTest |

---

## Architecture in one paragraph

**Modular monolith + vertical slices.** Seven modules deploy as a single process. Each module owns its
`DbContext`, its `Domain/` entities, and its `Features/` folders. Modules communicate **only** through
their `.Contracts` projects — never through runtime project references. A feature is one folder: a
Command/Query in Contracts, a handler, a validator, and a thin endpoint. No service layers, no
repository wrappers, no round trips through shared interfaces.

---

## Golden rules (always apply — non-negotiable)

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

---

## Domain rules for this clinic

| Rule | Why |
|---|---|
| Consent required at patient registration | Legal requirement; no consent = no record |
| Soft-delete only for patient/clinical data | Audit trail and regulatory retention |
| Prescriptions need an active Encounter | A script without a consult is a data integrity violation |
| Pharmacist can view/dispense; cannot create Encounters | Role boundary: clinical vs. pharmacy |
| Doctor cannot approve their own billing | Separation of duties (future billing module) |
| Appointment reminders check `ConsentToCommunications` | SMS/email requires explicit opt-in |

---

## Module inventory & ownership

| Module | Status | Key entities | Owned by |
|---|---|---|---|
| Patients | 🟡 In progress | Patient | Part 1–2 of article series |
| Appointments | ⬜ Planned | Appointment, Slot | Part 3 |
| Encounters | ⬜ Planned | Encounter, Diagnosis, Vital | Part 4 |
| Prescriptions | ⬜ Planned | Prescription, PrescriptionItem | Part 5 |
| Identity | ⬜ Planned | ClinicUser, Role, Permission | Part 6 |
| Billing | ⬜ Planned | Invoice, LineItem, Payment | Part 7 |
| Notifications | ⬜ Planned | Notification, DeliveryLog | Part 8 |

---

## Build & verify

```bash
dotnet build
dotnet test
dotnet run --project src/Host/MedClinic.Api
```

**After any feature:** build → test → self-review against `.agents/rules/` relevant files.

---

## How to work here (for AI agents)

1. Read AGENTS.md (you are here) to understand the system.
2. Identify the task type → match it to a skill in `.agents/skills/`:
   - New bounded context → `add-module`
   - New entity/aggregate → `add-entity`
   - New feature (command/query/endpoint) → `add-feature`
   - Schema change → `create-migration`
   - Cross-module event → `add-integration-event`
   - Permission/role → `add-permission`
3. Load the referenced rules files for the domain (e.g., for clinical data: `phi-and-tenancy.md`, `auditing.md`, `database.md`).
4. Follow the skill recipe **exactly** — the steps are ordered to prevent silent failures.
5. After completing, run the `code-reviewer` and `phi-review` workflows to self-check.
6. **Never** modify `src/BuildingBlocks` without explicit human approval — see `buildingblocks-protection.md`.

---

## Rules index (`.agents/rules/`)

| File | When to load |
|---|---|
| `architecture.md` | Any file placement, module creation, boundary question |
| `api-conventions.md` | Adding an endpoint or handler |
| `database.md` | Adding an entity, query, or migration |
| `phi-and-tenancy.md` | Any file touching patient/clinical data |
| `auditing.md` | Encounters, Prescriptions, any clinical record access |
| `eventing.md` | Cross-module integration events or domain events |
| `security.md` | Auth, permissions, rate limiting, CORS |
| `logging.md` | Adding log statements anywhere |
| `testing.md` | Writing unit or integration tests |
| `buildingblocks-protection.md` | Before touching `src/BuildingBlocks` |
| `modules/patients.md` | Working in the Patients module |
| `modules/appointments.md` | Working in the Appointments module |
| `modules/encounters.md` | Working in the Encounters module |
| `modules/prescriptions.md` | Working in the Prescriptions module |
