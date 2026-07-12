namespace Core;

/// <summary>
/// Immutable record of a security-relevant event.
/// Written to the security_audit_log table — never to application logs (which may be shipped externally).
/// Never store PHI fields (email, name, phone) — only IDs and outcome categories.
/// </summary>
public sealed class SecurityAuditLog
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Outcome { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private SecurityAuditLog() { }

    public static SecurityAuditLog Record(
        Guid tenantId,
        string eventType,
        string outcome,
        TimeProvider clock,
        Guid? userId = null,
        string? detail = null,
        string? ipAddress = null) => new()
    {
        TenantId  = tenantId,
        UserId    = userId,
        EventType = eventType,
        Outcome   = outcome,
        Detail    = detail,
        IpAddress = ipAddress,
        OccurredAt = clock.GetUtcNow(),
    };
}

/// <summary>Well-known security event types.</summary>
public static class SecurityEvents
{
    public const string LoginSuccess       = "auth.login.success";
    public const string LoginFailed        = "auth.login.failed";
    public const string TokenRefreshed     = "auth.token.refreshed";
    public const string TokenRefreshFailed = "auth.token.refresh_failed";
    public const string Impersonation      = "auth.impersonation";
    public const string PermissionDenied   = "authz.permission_denied";
    public const string PasswordChanged    = "auth.password.changed";
    public const string AccountLocked      = "auth.account.locked";
}
