# MediClinic

**A production SaaS for private medical clinics — and a reference implementation for AI-native .NET development.**

Built on [fullstackhero](https://fullstackhero.net) architecture: modular monolith, vertical slices, multi-tenant, PHI-safe by default. Documented as a [Medium article series](#article-series) so every decision is explainable and reproducible.

---

## What this is

A clinic management system for a **single-doctor private practice**: one GP, one pharmacist, a small patient population. Not a hospital system. Scope is deliberately small so the architecture stays teachable.

It is also a **living reference** for how to build modern enterprise APIs with AI assistance — showing the AGENTS.md / skills / workflows pattern in a real, production-grade codebase.

---

## Domain modules

| Module | Purpose | Status |
|---|---|---|
| **Patients** | Demographics, consent, allergies | 🟡 In progress |
| **Appointments** | Scheduling, check-in/check-out lifecycle | ⬜ Planned |
| **Encounters** | Clinical notes, diagnoses (ICD-10), vitals | ⬜ Planned |
| **Prescriptions** | Drug orders, pharmacist workflow | ⬜ Planned |
| **Identity** | JWT auth, roles (Doctor/Pharmacist/Receptionist/Admin) | ⬜ Planned |
| **Billing** | Consultation fees, invoices, payment status | ⬜ Planned |
| **Notifications** | Appointment reminders (consent-gated) | ⬜ Planned |

---

## Tech stack

**.NET 10** · Minimal APIs · Source-generated Mediator · FluentValidation · EF Core 10 · PostgreSQL · Finbuckle multitenancy · Serilog · Scalar

---

## Architecture

Modular monolith + vertical slices. Modules communicate only through their `.Contracts` projects. Every feature is one folder: Command/Query → Handler → Validator → Endpoint. No repository wrappers, no service layers.

```
src/
├── BuildingBlocks/          # Result<T>, BaseDbContext, ITenantContext, ValidationFilter
├── Host/
│   ├── MedClinic.Api        # Composition root
│   └── MedClinic.DbMigrator # Runs migrations at deploy time (never at API startup)
└── Modules/
    ├── Patients/
    ├── Appointments/
    ├── Encounters/
    ├── Prescriptions/
    ├── Identity/
    ├── Billing/
    └── Notifications/
```

---

## Quick start

```bash
# Prerequisites: .NET 10 SDK, Docker (for PostgreSQL)
git clone https://github.com/<you>/MediClinic
cd MediClinic
dotnet build
dotnet run --project src/Host/MedClinic.Api
# API docs at https://localhost:5001/scalar
```

---

## AI development layer

This repo is built **with** AI, not just for humans. The `.agents/` directory contains:

| Path | Purpose |
|---|---|
| `.agents/rules/` | Convention files loaded on demand — architecture, PHI, auditing, security, logging, testing |
| `.agents/skills/` | Task recipes — `add-feature`, `add-entity`, `add-module`, `create-migration`, `add-integration-event`, `add-permission` |
| `.agents/workflows/` | Orchestration + review playbooks — `feature-scaffolder`, `architecture-guard`, `phi-review`, `code-reviewer` |
| `AGENTS.md` | Canonical guide (tool-neutral — imported by CLAUDE.md, GEMINI.md, .cursorrules) |

**Using Claude Code?** Read `CLAUDE.md`. **Using Cursor or Gemini CLI?** The same `AGENTS.md` is your entry point.

---

## Article series

*Building a Production SaaS with AI: A MediClinic Reference Implementation* — published on Medium.

| Part | Title | Tag |
|---|---|---|
| 0 | The Blueprint — Architecture Decisions Before a Single Line of Code | `article/part-0` |
| 1 | Building Blocks — The Shared Infrastructure Every Module Depends On | `article/part-1` |
| 2 | Your First Vertical Slice — Registering a Patient with Full PHI Safety | `article/part-2` |
| 3 | State Machines in Practice — The Appointment Booking & Check-In Lifecycle | `article/part-3` |
| 4 | Handling PHI at the Code Level — Clinical Notes and the Audit Trail | `article/part-4` |
| 5 | The Pharmacist's Workflow — Drug Orders, Allergy Checks, and Dispensing | `article/part-5` |
| 6 | JWT Auth, Role-Based Permissions, and Multi-Tenant Security in .NET 10 | `article/part-6` |
| 7 | Simple Billing for a Private Clinic | `article/part-7` |
| 8 | Consent-Gated Reminders — Sending Notifications the Right Way | `article/part-8` |
| 9 | How to Test a Multi-Tenant SaaS | `article/part-9` |
| 10 | AI-Native SaaS — How AGENTS.md, Skills, and Workflows Accelerate Every Feature | `article/part-10` |

Each article has a corresponding `git tag article/part-N`. Check out any tag to see the exact code state for that article.

---

## License

MIT
