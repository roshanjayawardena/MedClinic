# BuildingBlocks protection — read before touching `src/BuildingBlocks`

`src/BuildingBlocks/` (Core, Persistence, Web, and later Eventing, Caching, Storage…) is shared by
**every** module. A change here has a wide blast radius: a subtle edit to `BaseDbContext` or
`AuditableEntity` can break tenant isolation or auditing across all clinics at once. Treat it as
protected infrastructure, not everyday code.

## The rule

**Do not modify `src/BuildingBlocks` without explicit approval.** When a task seems to need a
BuildingBlocks change, stop and confirm with the human first — describe what you'd change and why, and
wait for a yes. Default to solving the problem inside the module instead.

## Modify vs. create (an important distinction)

- **Creating** the foundation that's meant to exist but is still a placeholder (e.g. replacing
  `Persistence/Class1.cs` with the intended `BaseDbContext`) is expected work — do it deliberately, in
  its own change, with review.
- **Modifying** behavior other modules already depend on (changing how the tenant filter works, the
  shape of `Result`, the audit fields) is the dangerous case that needs explicit approval.

If you're unsure which you're doing, assume it's the dangerous case and ask.

## Before any approved BuildingBlocks change

- State the blast radius: which modules and behaviors are affected.
- Prefer **additive** changes (a new overload, a new opt-in interface) over changing or removing
  existing signatures and behavior.
- Never quietly change a default that modules rely on (tenant filtering ON, soft-delete behavior,
  audit stamping). If a default must change, call it out loudly.
- Keep `Result`, `ITenantContext`, `AuditableEntity`, and `BaseDbContext` contracts stable — these are
  load-bearing across the whole solution.

## After any approved change

- `dotnet build` the whole solution (warnings are errors).
- Run the **architecture tests** and the full test suite — a BuildingBlocks change is exactly when
  cross-module breakage hides.
- Sanity-check tenant isolation still holds: a query in one clinic must not see another clinic's rows.

## What does NOT belong in BuildingBlocks

- Anything domain-specific (Patient, Appointment, Encounter logic) — that lives in its module.
- One module's convenience helper that no other module needs.
- PHI-aware logic — health-domain rules belong with the modules and in `phi-and-tenancy.md`,
  not baked into shared infrastructure.

> Rule of thumb: BuildingBlocks holds *mechanism* (how persistence, results, tenancy work), never
> *policy* (what a clinic or a patient record means). If your change encodes a domain decision, it's in
> the wrong place.
