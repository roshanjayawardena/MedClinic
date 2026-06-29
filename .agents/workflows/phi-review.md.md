# PHI review (run before accepting a diff that touches patient data)

Review the current changes against .agents/rules/phi-and-tenancy.md and report:
- Any query missing a tenant filter.
- Any log statement that could emit PHI.
- Any read/write of a clinical record that doesn't emit an audit event.
Emit a short structured report. Do not modify code.