namespace Identity.Domain;

/// <summary>
/// Maps each role to its set of fine-grained permissions.
/// These permissions are embedded in the JWT at login time — no DB lookup per request.
/// Trade-off: permission changes take effect only on next login (token expiry).
/// </summary>
public static class RolePermissions
{
    public static readonly IReadOnlyDictionary<string, string[]> ByRole =
        new Dictionary<string, string[]>
        {
            [Roles.Doctor] =
            [
                Permissions.PatientsRead,
                Permissions.AppointmentsRead,
                Permissions.EncountersCreate,
                Permissions.EncountersRead,
                Permissions.EncountersUpdate,
                Permissions.PrescriptionsWrite,
                Permissions.PrescriptionsRead,
            ],
            [Roles.Pharmacist] =
            [
                Permissions.PatientsRead,
                Permissions.PrescriptionsRead,
                Permissions.PrescriptionsDispense,
            ],
            [Roles.Receptionist] =
            [
                Permissions.PatientsRegister,
                Permissions.PatientsRead,
                Permissions.AppointmentsCreate,
                Permissions.AppointmentsRead,
            ],
            [Roles.Admin] =
            [
                Permissions.PatientsRegister,
                Permissions.PatientsRead,
                Permissions.AppointmentsCreate,
                Permissions.AppointmentsRead,
                Permissions.EncountersCreate,
                Permissions.EncountersRead,
                Permissions.EncountersUpdate,
                Permissions.PrescriptionsWrite,
                Permissions.PrescriptionsRead,
                Permissions.PrescriptionsDispense,
                Permissions.UsersManage,
            ],
        };
}
