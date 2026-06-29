namespace Core;

/// <summary>
/// A tenant-scoped audit record for a read or write of clinical/PHI data.
/// Never populate Details with PHI — surrogate ids only.
/// </summary>
public sealed record AuditEntry(
    Guid Id,
    Guid TenantId,
    string Action,
    string EntityType,
    string EntityId,
    string? PerformedBy,
    DateTimeOffset OccurredAtUtc);
