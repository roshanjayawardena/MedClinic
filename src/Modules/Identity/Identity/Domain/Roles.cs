namespace Identity.Domain;

public static class Roles
{
    public const string Doctor = "Doctor";
    public const string Pharmacist = "Pharmacist";
    public const string Receptionist = "Receptionist";
    public const string Admin = "Admin";

    public static readonly string[] All = [Doctor, Pharmacist, Receptionist, Admin];
}
