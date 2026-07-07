namespace Core;

/// <summary>
/// Base class for every domain entity that requires tenant isolation,
/// soft-delete, and audit timestamps. BaseDbContext stamps all these
/// fields automatically on SaveChanges — handlers never set them directly.
/// </summary>
public abstract class AuditableEntity
{
    protected AuditableEntity() { } // parameterless ctor required by EF Core

    /// <summary>Surrogate primary key. Set by the derived entity's factory method.</summary>
    public Guid Id { get; protected set; }

    /// <summary>Clinic that owns this record. Set by BaseDbContext on insert.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>UTC timestamp of first insert. Set by BaseDbContext.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>UTC timestamp of last update. Null until first modification.</summary>
    public DateTimeOffset? ModifiedAt { get; private set; }

    /// <summary>True if the record has been soft-deleted. Never hard-delete clinical data.</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>UTC timestamp of soft-delete. Set by BaseDbContext when IsDeleted → true.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }
}
