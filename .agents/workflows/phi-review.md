# phi-review (read-only) — MedClinic health-domain gate

Run this whenever the diff touches patient, appointment, encounter, or prescription code.
Review against `phi-and-tenancy.md` and `auditing.md`. **Do not modify code.**

Check, citing file + line:
- **Tenant scoping:** is every query over patient/clinical data scoped to the current clinic? Flag any
  `IgnoreQueryFilters()`, raw SQL, or cross-tenant join.
- **PHI in logs/exceptions/telemetry:** any name, DOB, contact, address, diagnosis, or medication in a
  log message, exception text, or span attribute? (A surrogate GUID is fine; PHI is not.)
- **Consent:** does any outreach path (reminder, email, SMS) check the patient's communication consent?
- **Audit:** does every read or write of an Encounter or Prescription emit an audit event (who, action,
  entity, clinic, UTC) via the Outbox?
- **Deletion:** is clinical data soft-deleted (flagged + audited), never hard-deleted?

Report format: Must-fix / Should-fix, each with file:line and the rule it violates. End with a one-line
verdict (PHI-safe / not PHI-safe). No code edits.
