# PHI handling & tenant isolation (read before touching patient data)

## Tenant isolation
- A clinic is a tenant. Patient, appointment, encounter, and prescription
  data belong to exactly one tenant.
- Every query that touches this data MUST filter by the current tenant.
  Finbuckle provides the tenant context; never bypass it.
- BAD:  db.Patients.Where(p => p.Id == id)
- GOOD: db.Patients.Where(p => p.Id == id)        // tenant filter applied
        // by the module's DbContext global query filter — never remove it.

## PHI in logs
- Treat name, date of birth, contact info, and any clinical detail as PHI.
- Structured logs may include a patient's surrogate Id, never PHI fields.
- Use the redaction helper; do not string-interpolate PHI into log messages.

## Audit
- Reading or modifying an Encounter or Prescription MUST publish an
  audit event (who, action, entity, tenant, UTC time) via the outbox.