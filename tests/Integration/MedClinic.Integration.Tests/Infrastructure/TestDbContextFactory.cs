using Microsoft.EntityFrameworkCore;

namespace MedClinic.Integration.Tests.Infrastructure;

/// <summary>
/// Minimal IDbContextFactory&lt;T&gt; implementation for integration tests.
/// Wraps a factory function so tests can supply a fresh DbContext per handler invocation,
/// matching the IDbContextFactory pattern used by every production handler.
/// </summary>
public sealed class TestDbContextFactory<TContext>(Func<TContext> factory)
    : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => factory();

    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(factory());
}
