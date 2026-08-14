namespace Identity.Domain;

public static class Roles
{
    public const string Doctor       = "Doctor";
    public const string Pharmacist   = "Pharmacist";
    public const string Receptionist = "Receptionist";
    public const string Admin        = "Admin";

    /// <summary>
    /// Cross-tenant system administrator. JWT for this role has no clinic_id claim,
    /// so TenantClaimValidationMiddleware does not block tenant-less requests.
    /// </summary>
    public const string SystemAdmin  = "SystemAdmin";

    public static readonly string[] All        = [Doctor, Pharmacist, Receptionist, Admin];
    public static readonly string[] AllSystem  = [..All, SystemAdmin];
}
