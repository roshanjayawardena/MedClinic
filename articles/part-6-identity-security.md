# Part 6: JWT Auth, Role-Based Permissions, and Multi-Tenant Security in .NET 10

*Building a MediClinic SaaS — Part 6 of an ongoing series*

---

Every module we've built so far has a silent assumption baked in: that only authorised, authenticated users call it, and that a user from Clinic A can never read Clinic B's data. It was time to make that assumption load-bearing.

This article builds the Identity module — the security foundation that every other module depends on. We cover:

1. **JWT over cookies** — and why it's the right default for a multi-tenant SaaS API
2. **`clinic_id` as the tenant security boundary** — how it's embedded in the token and enforced per request
3. **The cross-tenant attack and the middleware that stops it** — a concrete example of a real vulnerability and its fix
4. **Role-to-permission mapping embedded in the token** — trade-offs of this approach
5. **`AddIdentityCore` vs `AddIdentity`** — why the difference matters
6. **Rate limiting on auth endpoints** — brute-force protection in five lines

---

## Why JWT, not cookies

For a browser-only monolith with one tenant, session cookies work beautifully. For a multi-tenant SaaS API that serves mobile apps, CLI tools, and multiple front-ends:

| Concern | Cookies | JWT |
|---|---|---|
| Statefulness | Server-side session store | Stateless — self-contained token |
| Cross-domain | CORS complexity | Just pass the header |
| Multi-tenant isolation | Session tied to one origin | `clinic_id` claim travels with every request |
| Mobile clients | Cookie jar management | `Authorization: Bearer <token>` works everywhere |
| Horizontal scaling | Sticky sessions or shared session store | Any instance validates any token |

The statelessness of JWT is also its weakness: revocation before expiry requires either a short lifetime or a deny-list. We use a 60-minute expiry with no refresh token for simplicity. A production system would add refresh tokens (Part 9).

---

## The JWT structure

Every token issued by MediClinic carries:

```json
{
  "sub": "a3f2e1c0-...",
  "email": "dr.smith@eastside.clinic",
  "given_name": "Sarah",
  "family_name": "Smith",
  "clinic_id": "d9a7b2f4-...",
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "Doctor",
  "permissions": [
    "Patients.Read",
    "Appointments.Read",
    "Encounters.Create",
    "Encounters.Read",
    "Encounters.Update",
    "Prescriptions.Write",
    "Prescriptions.Read"
  ],
  "nbf": 1751234567,
  "exp": 1751238167,
  "iss": "MediClinic",
  "aud": "MediClinic"
}
```

The `clinic_id` is the critical claim. It answers: *which clinic does this authenticated user belong to?* The four-module data stack (Patients, Appointments, Encounters, Prescriptions) uses `X-Tenant-Id` for tenant scoping. The JWT `clinic_id` must match.

---

## The cross-tenant attack

Without an explicit check, a legitimate user of Clinic A can exploit the system like this:

1. Log in to Clinic A. Receive a valid JWT with `clinic_id: A`.
2. Call `GET /patients/{id}` with `X-Tenant-Id: B` in the header.
3. `HttpTenantContext` resolves the tenant from the header → ClinicB.
4. EF global query filter: `patient.TenantId == tenantContext.TenantId` → `TenantId == B`.
5. If the patient ID exists in Clinic B, **the query returns their data**.

The user's JWT says Clinic A but the query runs against Clinic B. This is a cross-tenant data leak.

The fix is `TenantClaimValidationMiddleware`:

```csharp
public sealed class TenantClaimValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claimedClinicId = context.User.FindFirst("clinic_id")?.Value;
            var headerClinicId = context.Request.Headers["X-Tenant-Id"].ToString();

            if (!string.IsNullOrEmpty(claimedClinicId)
                && !string.IsNullOrEmpty(headerClinicId)
                && !string.Equals(claimedClinicId, headerClinicId, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync(
                    "Forbidden: JWT clinic_id does not match X-Tenant-Id header.");
                return;
            }
        }

        await next(context);
    }
}
```

The middleware runs after `UseAuthentication()` (so `context.User` is populated) but before endpoint handlers. A `403 Forbidden` stops the request before it reaches any module handler. The EF tenant filter is now a *second* line of defence rather than the only one.

```csharp
// Program.cs middleware order — order matters
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantClaimValidationMiddleware>();  // ← clinic_id == X-Tenant-Id
app.UseAuthorization();
// then MapEndpoints
```

---

## `AddIdentityCore` vs `AddIdentity`

The standard `AddIdentity<TUser, TRole>()` registers ASP.NET Core Identity **and** cookie authentication middleware as the default scheme. In a JWT-only API, this causes `RequireAuthorization()` to redirect to `/Account/Login` instead of returning 401.

`AddIdentityCore<TUser>()` registers only the user management services — `UserManager<T>`, `IPasswordHasher<T>`, validators — without touching authentication schemes. We then add JWT authentication separately:

```csharp
services.AddIdentityCore<ClinicUser>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
})
.AddRoles<ClinicRole>()
.AddEntityFrameworkStores<IdentityModuleDbContext>()
.AddDefaultTokenProviders();

services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(configuration["Jwt:Secret"]!)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
```

`ClockSkew = TimeSpan.FromSeconds(30)` is a common gotcha: the default is five minutes, meaning a token technically expired five minutes ago is still accepted. We tighten this to 30 seconds.

---

## The IdentityModuleDbContext: one subtle trap

`IdentityDbContext<TUser, TRole, TKey>` is the base class for our context. It exposes built-in DbSet properties including `Roles` (type `DbSet<TRole>`). Inside `OnModelCreating`, when you write `Roles.Doctor`, C# resolves `Roles` as the inherited `Roles` property (the DbSet), not your static `Identity.Domain.Roles` class.

The fix: use a `using` alias to disambiguate:

```csharp
using DomainRoles = Identity.Domain.Roles;

// Inside OnModelCreating:
builder.Entity<ClinicRole>().HasData(
    new ClinicRole(DomainRoles.Doctor)      { Id = new Guid("11111111-0000-0000-0000-000000000001"), ... },
    new ClinicRole(DomainRoles.Pharmacist)  { Id = new Guid("11111111-0000-0000-0000-000000000002"), ... },
    ...
);
```

The roles are seeded in the migration with stable GUIDs. This is important: EF's `HasData` seeding is idempotent by PK, so running migrations twice won't create duplicates. The `ConcurrencyStamp` must also be deterministic for seeds — using `Guid.NewGuid()` would produce a different value on every model compilation, triggering false migration diffs.

---

## Tenant-scoped user management

`IdentityModuleDbContext` applies a query filter to `ClinicUser`:

```csharp
builder.Entity<ClinicUser>()
    .HasQueryFilter(u => u.ClinicId == tenantContext.TenantId);
```

`UserManager.FindByEmailAsync` uses this DbContext. When a login request arrives with `X-Tenant-Id: ClinicA` and email `dr.smith@eastside.clinic`, the query filters by `ClinicId = ClinicA`. The same email in Clinic B is invisible — a completely different record as far as this query is concerned. Tenant isolation reaches all the way into the Identity layer.

---

## Permission embedding: trade-offs

Permissions are embedded in the JWT at login time:

```csharp
var permissions = roles
    .SelectMany(role => RolePermissions.ByRole.TryGetValue(role, out var perms) ? perms : [])
    .Distinct()
    .ToArray();

claims.AddRange(permissions.Select(p => new Claim("permissions", p)));
```

Policy-based authorization then checks these claims:

```csharp
services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission, policy =>
            policy.RequireClaim("permissions", permission));
    }
});
```

And endpoints declare what they require:

```csharp
app.MapGet("/auth/me", ...).RequireAuthorization();  // any authenticated user
app.MapPost("/encounters", ...).RequireAuthorization(Permissions.EncountersCreate);
app.MapPost("/prescriptions/{id}/dispense", ...).RequireAuthorization(Permissions.PrescriptionsDispense);
```

**Trade-off table:**

| Embedding permissions in JWT | Database lookup per request |
|---|---|
| Zero DB round-trips per request | Real-time permission enforcement |
| Permissions stale until token expiry | Permission revoked immediately |
| Larger token (a few hundred extra bytes) | Extra latency per request |
| Correct for slowly-changing role boundaries | Required for fine-grained revocation |

For a clinic with four fixed roles and no per-user overrides, embedding is the right call. A future "custom roles" feature would require either short token lifetimes or a lookup.

---

## The role model

| Role | Key permissions |
|---|---|
| Doctor | `Encounters.Create`, `Encounters.Update`, `Prescriptions.Write`, `Patients.Read` |
| Pharmacist | `Prescriptions.Read`, `Prescriptions.Dispense`, `Patients.Read` |
| Receptionist | `Patients.Register`, `Appointments.Create`, `Appointments.Read` |
| Admin | All of the above + `Users.Manage` |

Notice what pharmacists can and cannot do:
- **Can**: read prescriptions, dispense them
- **Cannot**: create encounters, write prescriptions, register patients

This enforces the domain rule from Part 5: a prescription requires a closed encounter written by a doctor. Even if someone crafted a `WritePrescription` API call as a pharmacist user, `RequireAuthorization(Permissions.PrescriptionsWrite)` would reject it before the handler runs.

---

## Rate limiting: five lines to brute-force protection

```csharp
services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", window =>
    {
        window.Window = TimeSpan.FromMinutes(1);
        window.PermitLimit = 10;
        window.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
```

Applied to auth endpoints via `.RequireRateLimiting("auth")`:

```csharp
app.MapPost("/auth/login", ...).RequireRateLimiting("auth").AllowAnonymous();
app.MapPost("/auth/register", ...).RequireRateLimiting("auth").AllowAnonymous();
```

Ten login attempts per IP per minute. An attacker trying to brute-force a password would need 10 minutes per 10 guesses — effectively throttled. A production system would add distributed rate limiting (Redis-backed) and IP-based lockout, but this catches the basic case with no infrastructure changes.

The `app.UseRateLimiter()` middleware runs before `UseAuthentication()` so the limiter sees all requests, including those with bad or missing tokens.

---

## The login flow end-to-end

```
POST /auth/login
X-Tenant-Id: d9a7b2f4-...
{ "email": "dr.smith@clinic.com", "password": "••••••••" }

1. Rate limiter: check request count for this IP (10 req/min)
2. FluentValidation: email format, password length
3. LoginHandler:
   a. UserManager.FindByEmailAsync  →  applies ClinicId query filter
   b. UserManager.CheckPasswordAsync →  bcrypt verification
   c. UserManager.GetRolesAsync      →  ["Doctor"]
   d. JwtService.GenerateToken       →  embeds clinic_id, roles, permissions
4. Response: { accessToken: "eyJ...", tokenType: "Bearer", expiresIn: 3600 }
```

Subsequent calls:
```
GET /encounters/abc
Authorization: Bearer eyJ...
X-Tenant-Id: d9a7b2f4-...

1. Rate limiter: no limiter on this endpoint, pass-through
2. UseAuthentication: parse JWT, populate HttpContext.User
3. TenantClaimValidationMiddleware: clinic_id claim == X-Tenant-Id header ✓
4. UseAuthorization: check "Encounters.Read" policy → permission in JWT claims ✓
5. GetEncounterByIdHandler: EF query with TenantId filter → correct clinic's data
```

---

## ICurrentUser and the PerformedBy gap

Every `AuditEntry` we've written so far has `PerformedBy: null`. The field exists — we just haven't had an Identity module to populate it from. The `ICurrentUser` service bridges that gap:

```csharp
public interface ICurrentUser
{
    Guid UserId { get; }
    Guid ClinicId { get; }
    string Email { get; }
    string FirstName { get; }
    string LastName { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool IsAuthenticated { get; }
    bool HasPermission(string permission);
}
```

The implementation (`CurrentUser`) reads from `IHttpContextAccessor` — no database call, because all the data is already in the JWT claims.

To wire it into, say, `WritePrescriptionHandler`, you'd inject `ICurrentUser` and pass the user ID to the audit entry:

```csharp
db.AuditEntries.Add(new AuditEntry(
    Guid.NewGuid(),
    tenantContext.TenantId,
    Action: "PrescriptionWritten",
    EntityType: nameof(Prescription),
    EntityId: prescription.Id.ToString(),
    PerformedBy: currentUser.UserId.ToString(),  // ← now populated
    timeProvider.GetUtcNow()));
```

Wiring `ICurrentUser` into every existing handler is a refactor worth doing — just not in this article. The architectural machinery is in place. The next step is mechanical.

---

## Running the migration

```bash
dotnet ef migrations add InitialIdentityCreate \
  --context IdentityModuleDbContext \
  --project src/Host/MedClinic.Migrations.PostgreSQL \
  --startup-project src/Host/MedClinic.Migrations.PostgreSQL \
  --output-dir Migrations/Identity

dotnet run --project src/Host/MedClinic.DbMigrator
```

The migration creates the full ASP.NET Core Identity schema in the `identity` schema: `AspNetUsers` (configured as `identity.AspNetUsers`), `AspNetRoles`, `AspNetUserRoles`, etc. — plus the four seeded roles.

To bootstrap your first admin user:

```bash
POST /auth/register
X-Tenant-Id: <your-clinic-guid>
{
  "email": "admin@yourclinic.com",
  "password": "SecurePass1",
  "firstName": "Admin",
  "lastName": "User",
  "role": "Admin"
}
```

Then log in:

```bash
POST /auth/login
X-Tenant-Id: <your-clinic-guid>
{ "email": "admin@yourclinic.com", "password": "SecurePass1" }
```

The response carries your JWT. All subsequent requests include `Authorization: Bearer <token>`.

---

## What we built

| Artifact | Purpose |
|---|---|
| `ClinicUser` extends `IdentityUser<Guid>` | User entity with `ClinicId` tenant field |
| `ClinicRole` extends `IdentityRole<Guid>` | Role entity — Doctor, Pharmacist, Receptionist, Admin |
| `Roles` / `Permissions` / `RolePermissions` | Domain constants mapping roles to fine-grained permissions |
| `IdentityModuleDbContext` | EF context with `ClinicId` query filter + seeded roles |
| `JwtService` | Generates token with sub, clinic_id, roles, permissions claims |
| `ICurrentUser` / `CurrentUser` | Reads authenticated user from JWT claims without DB |
| `TenantClaimValidationMiddleware` | Blocks requests where JWT clinic_id ≠ X-Tenant-Id header |
| `LoginHandler` | Validates credentials, issues JWT |
| `RegisterUserHandler` | Creates user with role in the current tenant |
| `GetCurrentUserHandler` | Returns the JWT-derived user profile |
| Rate limiter | 10 req/min on auth endpoints |
| Authorization policies | One policy per permission — `RequireClaim("permissions", value)` |

---

## What's next

Part 7 covers **Billing** — the module that subscribes to `PrescriptionDispensedIntegrationEvent` and creates an invoice. We'll demonstrate the full event-driven flow: dispense a prescription in Part 5's module, watch Part 7's module receive the event and create a consultation fee, and close the loop with a payment status transition.

The `PerformedBy` field in audit entries will also finally be populated — after wiring `ICurrentUser` into the handlers.
