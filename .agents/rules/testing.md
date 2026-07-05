# Testing — unit tests, integration tests, architecture tests

Read this before writing any test.

## Test project layout

```
tests/
├── Architecture/
│   └── MedClinic.Architecture.Tests/    # NetArchTest — boundary enforcement
└── Integration/
    ├── Patients.Integration.Tests/       # One project per module
    ├── Appointments.Integration.Tests/
    └── Encounters.Integration.Tests/
```

No shared "unit test" project — unit tests live alongside the code they test is rarely needed here.
Handlers and validators are pure functions; test them via integration tests with a real database.

## Architecture tests (always keep green)

Architecture tests in `tests/Architecture/` enforce the golden rules mechanically:

```csharp
// Example: modules must not reference each other's runtime project
[Fact]
public void Modules_ShouldNot_Reference_Each_Others_Runtime()
{
    var result = Types.InCurrentDomain()
        .That().ResideInNamespace("MedClinic.Modules")
        .ShouldNot().HaveDependencyOnAny(RuntimeModuleNamespaces)
        .GetResult();

    result.IsSuccessful.Should().BeTrue();
}
```

Add a test here whenever you add a new module or a new convention you want mechanically enforced.
Run after every module addition: `dotnet test tests/Architecture`.

## Integration tests

Use **Testcontainers** to spin up a real PostgreSQL instance for each test run. No mocked `DbContext`.

```csharp
public class RegisterPatientTests(PatientsFixture fixture) : IClassFixture<PatientsFixture>
{
    [Fact]
    public async Task RegisterPatient_WithConsent_ReturnsPatientId()
    {
        var cmd = new RegisterPatientCommand(
            FirstName: "Test", LastName: "Patient",
            DateOfBirth: new DateOnly(1985, 3, 12),
            ConsentToDataProcessing: true);

        var result = await fixture.SendAsync(cmd);

        result.IsSuccess.Should().BeTrue();
        result.Value.PatientId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RegisterPatient_WithoutConsent_Fails()
    {
        var cmd = new RegisterPatientCommand(..., ConsentToDataProcessing: false);
        var result = await fixture.SendAsync(cmd);
        result.IsFailure.Should().BeTrue();
    }
}
```

## Test fixture conventions

- One `*Fixture` class per module test project — sets up Testcontainers, applies migrations, seeds tenant.
- Fixtures implement `IAsyncLifetime`; PostgreSQL starts in `InitializeAsync`, tears down after the run.
- Each test gets a **fresh tenant** (new `ClinicId` GUID) to prevent state bleed between tests.
- Use `fixture.SendAsync(command)` to go through the full Mediator pipeline, including validators.

## What to test

| Scenario | What to assert |
|---|---|
| Happy path | Result is success; entity exists in DB with correct state |
| Validation failure | Result is failure with expected error message; nothing written to DB |
| Aggregate invariant violated | Handler returns `Result.Fail` (not throws); no partial write |
| Cross-module contract | Query across Contracts returns correct result; no runtime project dependency |
| Tenant isolation | Data created under tenant A is not visible to tenant B |

## What not to mock

- **Never mock `DbContext`** — use Testcontainers. Mocked EF misses migration drift, query filter gaps,
  and the actual SQL generated.
- **Never mock `TimeProvider`** — inject `FakeTimeProvider` (Microsoft.Extensions.Time.Testing) to
  control time in deterministic tests.
- Mock external I/O (email sending, SMS) — those are side effects, not the logic under test.

## Naming convention

`MethodUnderTest_Condition_ExpectedResult`

```
RegisterPatient_WithConsent_ReturnsPatientId
RegisterPatient_WithoutConsent_Fails
CheckIn_ForCancelledAppointment_ReturnsFailure
```

## Self-check

- [ ] New happy path + at least one failure case for every command handler.
- [ ] Tenant isolation test for any new query (tenant A cannot see tenant B's data).
- [ ] Architecture test updated if a new convention was introduced.
- [ ] No mocked `DbContext` — Testcontainers only.
