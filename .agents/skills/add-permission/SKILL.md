---
name: add-permission
description: >
  Use when adding a new permission constant, assigning it to a role's default set,
  or guarding an endpoint with a new permission check. Triggers on "add permission",
  "restrict this endpoint", "only doctors can", "pharmacist cannot".
---

# Add a permission

Read first: `security.md`.

## Permission naming convention

`<Module>.<Action>` — PascalCase both parts.

Examples: `Encounters.Create`, `Prescriptions.Dispense`, `Patients.ViewAllergies`, `Billing.ViewInvoice`

## Step 1 — Declare the constant

```csharp
// src/Modules/<Module>/<Module>.Contracts/Permissions/<Module>Permissions.cs
namespace <Module>.Contracts.Permissions;

public static class <Module>Permissions
{
    public const string Create  = "<Module>.Create";
    public const string Read    = "<Module>.Read";
    public const string Update  = "<Module>.Update";
    public const string Delete  = "<Module>.Delete";
    // Add your specific permission:
    public const string Dispense = "<Module>.Dispense";
}
```

Keep constants in Contracts so subscribers can reference them without touching the runtime project.

## Step 2 — Assign to roles

In the Identity module's seed/configuration, add the permission to the appropriate role(s):

| Permission | Doctor | Pharmacist | Receptionist | Admin |
|---|---|---|---|---|
| `Encounters.Create` | ✓ | | | |
| `Prescriptions.Dispense` | | ✓ | | |
| `Appointments.Book` | | | ✓ | ✓ |

Update `Identity/Seeding/RolePermissions.cs` (or the equivalent seed class).

## Step 3 — Guard the endpoint

```csharp
// In the module's MapEndpoints() or the specific feature endpoint
group.MapPost("/prescriptions/{id:guid}/dispense", DispensePrescription.Handle)
     .WithName("DispensePrescription")
     .RequireAuthorization(p => p.RequireClaim("permission", PrescriptionsPermissions.Dispense));
```

Use the constant — never hardcode the string at the call site.

## Step 4 — Test the guard

Add an integration test that:
1. Calls the endpoint with a token that has the permission → expects success.
2. Calls the endpoint with a token that lacks the permission → expects `403 Forbidden`.

## Step 5 — Document in security.md

If this permission represents a new role boundary (e.g., a previously unrestricted action is now
gated), update the role/permission table in `.agents/rules/security.md`.
