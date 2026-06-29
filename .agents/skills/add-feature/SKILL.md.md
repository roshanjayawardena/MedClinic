---
name: add-feature
description: >
  Use when adding a backend vertical slice to a module — a new command or
  query for patients, appointments, encounters, prescriptions. Triggers on
  "add feature", "create endpoint", "register a patient", "schedule an
  appointment".
---

# Add a vertical-slice feature

1. In <Module>.Contracts: a `record Command/Query` and a `record Response`.
2. In <Module> runtime: a `sealed Handler` that injects the module DbContext,
   returns `Result<Response>`, takes a CancellationToken, and stays
   tenant-scoped (see .agents/rules/phi-and-tenancy.md).
3. A FluentValidation validator for the command.
4. A thin Minimal API endpoint mapping the route with OpenAPI metadata and a
   validation filter.
5. If the entity changed, follow the create-migration steps.
6. Verify: dotnet build, then dotnet test. Self-review against the rules.