using Core;

namespace Clinics.Domain;

/// <summary>
/// Represents a registered clinic (tenant). Not derived from AuditableEntity because
/// clinics ARE the tenants — they are not scoped to a tenant themselves.
/// </summary>
public sealed class Clinic
{
    private Clinic() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>URL-safe unique slug, e.g. "sunrise-medical".</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Doctor's email — used for the daily digest. Never logged.</summary>
    public string ContactEmail { get; private set; } = string.Empty;

    /// <summary>IANA timezone id, e.g. "Asia/Colombo". Used to localise the digest.</summary>
    public string TimeZoneId { get; private set; } = "UTC";

    public ClinicPlan Plan { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public static Clinic Register(
        string name,
        string slug,
        string contactEmail,
        string timeZoneId,
        ClinicPlan plan,
        DateTimeOffset now) =>
        new()
        {
            Id            = Guid.NewGuid(),
            Name          = name,
            Slug          = slug.ToLowerInvariant(),
            ContactEmail  = contactEmail,
            TimeZoneId    = timeZoneId,
            Plan          = plan,
            IsActive      = true,
            CreatedAt     = now,
        };

    public Result Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
            return Result.Fail(new Error("Clinic.AlreadyInactive", "Clinic is already inactive."));

        IsActive       = false;
        DeactivatedAt  = now;
        return Result.Ok();
    }

    public Result Activate()
    {
        if (IsActive)
            return Result.Fail(new Error("Clinic.AlreadyActive", "Clinic is already active."));

        IsActive      = true;
        DeactivatedAt = null;
        return Result.Ok();
    }
}
