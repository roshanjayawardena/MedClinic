# architecture-guard — module boundary validator (read-only)

Run this after adding a module, after a large refactor, or any time module boundaries feel uncertain.
**Does not modify code.** Emits a structured compliance report only.

---

## What it checks

### 1. Module isolation — no runtime cross-references

Verify that no module's runtime project directly references another module's runtime project.
Only `.Contracts` references are allowed across module boundaries.

Check: inspect every `*.csproj` in `src/Modules/`. For each, scan `<ProjectReference>` entries.

**Violation pattern:**
```xml
<!-- VIOLATION — Appointments references Patients runtime -->
<ProjectReference Include="..\..\Patients\Patients\Patients.csproj" />

<!-- ALLOWED — Appointments references Patients Contracts -->
<ProjectReference Include="..\..\Patients\Patients.Contracts\Patients.Contracts.csproj" />
```

### 2. Contracts purity — no EF types in Contracts

No `.Contracts` project may reference `Microsoft.EntityFrameworkCore` or any persistence package.
Contracts hold records and interfaces only.

Check: inspect every `*.Contracts.csproj` for EF-related `<PackageReference>` entries.

### 3. Feature folder layout — vertical slice, not file-type folders

Each module's `Features/` directory must use one-folder-per-feature layout.
Flag any flat `Handlers/`, `Validators/`, or `Endpoints/` directories.

Expected: `Features/<FeatureName>/<FeatureName>Handler.cs`
Violation: `Features/Handlers/BookAppointmentHandler.cs`

### 4. Mediator assembly registration

Verify that every module's runtime assembly AND its Contracts assembly appear in the Mediator
`o.Assemblies` list in **both**:
- `src/Host/MedClinic.Api/Program.cs`
- `src/Host/MedClinic.DbMigrator/Program.cs`

A missing entry causes silent 404s — the handler is never discovered.

### 5. BuildingBlocks integrity

Verify no module-specific domain types have leaked into `src/BuildingBlocks/`.
BuildingBlocks holds mechanism (Result, BaseDbContext, ITenantContext), never policy.

Check: no `Patient`, `Appointment`, `Encounter`, `Prescription` type names in BuildingBlocks source.

### 6. Handler shape compliance

Sample (do not exhaustively scan) handler files. Check for:
- `public sealed` modifier
- Return type `ValueTask<Result<T>>` or `ValueTask<Result>`
- `.ConfigureAwait(false)` on awaits
- `CancellationToken` parameter present and forwarded

---

## Report format

```
## Architecture Guard Report — <date>

### VIOLATIONS (must fix before merge)
- [critical] Appointments/Appointments.csproj references Patients runtime project (line 12)
- [critical] Encounters/Features/Handlers/ flat folder detected — use per-feature folders

### WARNINGS (should fix)
- [warn] GetAppointmentsHandler missing .ConfigureAwait(false) on line 34
- [warn] AppointmentsModule not registered in DbMigrator/Program.cs

### PASSED
- Module isolation: Patients, Prescriptions, Identity ✓
- Contracts purity: all .Contracts projects ✓
- Feature folder layout: Patients ✓

Verdict: VIOLATIONS FOUND — do not merge until critical items are resolved.
```

---

## When to run

- After `add-module` (check new module is correctly isolated and registered)
- After any `<ProjectReference>` change
- Before opening a PR that touches module boundaries
- Periodically (monthly) as module count grows
