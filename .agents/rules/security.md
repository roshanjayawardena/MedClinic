# Security — auth, permissions, rate limiting, CORS

Read this before adding an endpoint, a new role, or any auth-related middleware.

## Authentication

- JWT Bearer tokens issued by the Identity module.
- Every non-public endpoint requires `.RequireAuthorization()`.
- Token carries: `sub` (ClinicUserId), `clinic_id` (tenant), `role`, `permissions[]`, `exp`.
- Tokens are **short-lived** (15 min access / 7 day refresh). Refresh is a dedicated endpoint.
- Never store the raw token in a log, exception message, or telemetry span attribute.

## Roles in this clinic

This is a two-person clinic: one doctor and one pharmacist. The pharmacist also handles front-desk
duties (booking, patient registration) — there is no separate Receptionist role.

| Role | Who | Responsibilities |
|---|---|---|
| `Admin` | Clinic owner (typically the Doctor) | Manage users, configure clinic settings, view all modules |
| `Doctor` | The GP | Create/close Encounters; write Prescriptions; view all patient data; book Appointments |
| `Pharmacist` | The pharmacist + front desk | Read/dispense Prescriptions; book Appointments; register Patients |

Roles are additive. In a solo-practice the doctor typically also has the `Admin` role.

## Permission model

Fine-grained permissions are named `<Module>.<Action>` (e.g., `Encounters.Create`, `Prescriptions.Dispense`).
Each role ships with a default permission set. Permissions can be granted per-user on top of role defaults.

### Default permission sets

| Permission | Doctor | Pharmacist | Admin |
|---|---|---|---|
| `Patients.Register` | ✓ | ✓ | ✓ |
| `Patients.Read` | ✓ | ✓ | ✓ |
| `Patients.Update` | ✓ | | ✓ |
| `Appointments.Book` | ✓ | ✓ | ✓ |
| `Appointments.Cancel` | ✓ | ✓ | ✓ |
| `Appointments.Read` | ✓ | ✓ | ✓ |
| `Encounters.Create` | ✓ | | ✓ |
| `Encounters.Read` | ✓ | ✓ | ✓ |
| `Encounters.Close` | ✓ | | ✓ |
| `Prescriptions.Create` | ✓ | | ✓ |
| `Prescriptions.Read` | ✓ | ✓ | ✓ |
| `Prescriptions.Dispense` | | ✓ | ✓ |
| `Prescriptions.Void` | ✓ | | ✓ |
| `Billing.View` | ✓ | | ✓ |
| `Billing.Manage` | | | ✓ |

Load the `add-permission` skill when adding a new permission or changing a role's defaults.

## Tenant isolation = security boundary

The `ClinicId` in the JWT claim is the source of truth for tenant resolution. Never trust a
`clinicId` in the request body for data access — the middleware sets the tenant from the validated
token claim. See `phi-and-tenancy.md` for enforcement.

## CORS

- Development: all origins allowed (Scalar, Postman, local frontend).
- Production: explicit allowlist from configuration — never `AllowAnyOrigin` in production.

```csharp
// appsettings.Production.json
"Cors": { "AllowedOrigins": ["https://clinic.example.com"] }
```

## Rate limiting

- Auth endpoints (login, refresh, forgot-password): **5 req/min per IP** — brute-force protection.
- All other authenticated endpoints: **60 req/min per user** (sliding window).
- Exceeding the limit returns `429 Too Many Requests` with a `Retry-After` header.

## Input validation

- FluentValidation runs via `ValidationFilter` before the handler is invoked.
- The filter returns `400 ValidationProblem` (RFC 9457) on failure.
- Never call `.Validate()` manually inside a handler — the filter covers it.

## Self-check

- [ ] Every endpoint has `.RequireAuthorization()` or `.AllowAnonymous()` (explicit, not accidental).
- [ ] Permission claims checked for any write to clinical data.
- [ ] `ClinicId` comes from the validated JWT, not from the request body.
- [ ] No token, password, or secret appears in logs, responses, or telemetry.
- [ ] Auth endpoints are rate-limited.
