# feature-scaffolder — orchestrate a full vertical slice

Use this workflow when delivering a complete feature from scratch: new entity + new command/query + migration.
It sequences the individual skills in the correct order and has a review gate at each step.

---

## When to use

The user asks for something like:
- "Add appointment booking end to end"
- "Build the check-in feature for appointments"
- "Add the encounter creation feature with vitals"

If only a **command or query** is needed on an existing entity, go directly to `add-feature`.
If only an **entity** is needed, go directly to `add-entity`.
This workflow is for the full slice: entity + feature + migration + tests.

---

## Step 0 — Identify context

Before writing any code, answer:
1. Which module owns this feature?
2. Does the entity already exist? (yes → skip Step 1)
3. Does the feature touch clinical data? (yes → load `phi-and-tenancy.md` and `auditing.md`)
4. Does completing the feature require publishing an integration event? (yes → plan Step 4a)
5. Does the feature require a new permission? (yes → plan Step 4b)

---

## Step 1 — Entity (if new) → `add-entity` skill

Follow `add-entity` skill exactly:
- Domain type in `Domain/`, inherits `AuditableEntity`.
- `IEntityTypeConfiguration<T>` in `Persistence/Configurations/`.
- `DbSet<T>` on the module DbContext.
- **Gate:** `dotnet build` must be clean before proceeding.

---

## Step 2 — Feature slice → `add-feature` skill

Follow `add-feature` skill exactly:
- Command/Query + Response in `.Contracts`.
- Handler — sealed, `ValueTask<Result<T>>`, tenant-scoped, aggregate methods for state changes.
- Validator — required for every command and paginated query.
- Endpoint — thin, OpenAPI metadata, validation filter, maps from `MapEndpoints()`.
- **Gate:** `dotnet build` clean.

---

## Step 3 — Migration → `create-migration` skill

Only if Step 1 added or changed an entity:
- Follow `create-migration` skill exactly — wrong `--context` corrupts the snapshot.
- Review generated SQL before applying.
- **Gate:** review the migration file; confirm no unexpected drops or missing tenant columns.

---

## Step 4a — Integration event (if cross-module reaction needed)

If another module must react to this feature completing:
- Follow `add-integration-event` skill.
- Update `eventing.md` catalogue.

## Step 4b — Permission (if new access control needed)

If the feature introduces a new role boundary:
- Follow `add-permission` skill.
- Update `security.md` role table.

---

## Step 5 — Tests

Write integration tests per `testing.md`:
- Happy path: command succeeds, entity in DB with correct state.
- Validation failure: validator rejects bad input; nothing written.
- At least one tenant isolation test if the feature reads data.
- If clinical data: a test confirming the audit event is emitted.

---

## Step 6 — Self-review (mandatory gates)

Run both read-only review workflows before declaring done:

**`code-reviewer`:** boundaries, placement, handler shape, validators, endpoint thinness, logging.
**`phi-review`** (only if feature touches patient/clinical data): tenant scoping, PHI in logs, audit, soft-delete.

A Must-fix finding = do not mark complete. Fix and re-run.

---

## Completion checklist

- [ ] `dotnet build` — 0 errors, 0 warnings
- [ ] `dotnet test` — all green (including new tests)
- [ ] `code-reviewer` — no Must-fix findings
- [ ] `phi-review` — PHI-safe verdict (if applicable)
- [ ] Migration reviewed and applied via DbMigrator
- [ ] `eventing.md` and `security.md` updated if events/permissions were added
