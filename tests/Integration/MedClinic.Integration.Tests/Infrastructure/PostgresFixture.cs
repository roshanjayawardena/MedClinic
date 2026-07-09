using Microsoft.EntityFrameworkCore;
using Patients.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace MedClinic.Integration.Tests.Infrastructure;

/// <summary>
/// Spins up a real PostgreSQL container for the test run.
/// Shared across all test classes in the collection to avoid repeated container starts.
///
/// Schema creation uses EnsureCreated(), not MigrateAsync():
/// - MigrateAsync() replays the migration history; suitable for production and CI smoke tests.
/// - EnsureCreated() creates the schema from the current EF model in one step; suitable for
///   integration tests where we care about model shape, not migration history.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("mediclinic_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Use a throw-away tenant context — schema creation doesn't need a real tenant.
        await using var db = BuildPatientsDbContext(Guid.Empty);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Creates a PatientsDbContext scoped to the given tenant.
    /// Tests call this with a unique tenantId so that data is isolated between test cases.
    /// </summary>
    public PatientsDbContext BuildPatientsDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<PatientsDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new PatientsDbContext(options, new TestTenantContext(tenantId), TimeProvider.System);
    }

    public TestDbContextFactory<PatientsDbContext> BuildPatientsDbContextFactory(Guid tenantId)
        => new(() => BuildPatientsDbContext(tenantId));
}
