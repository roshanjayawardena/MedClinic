namespace Core;

/// <summary>
/// Abstraction over the currently authenticated user.
/// Backed by HttpContext claims at the host level — modules depend only on this interface.
/// Mirrors ITenantContext so BaseDbContext can stamp CreatedById / ModifiedById
/// without any reference to the Identity module.
/// </summary>
public interface ICurrentUserContext
{
    /// <summary>
    /// The authenticated user's id, or <see cref="Guid.Empty"/> for anonymous / background jobs.
    /// </summary>
    Guid UserId { get; }

    bool IsAuthenticated { get; }
}
