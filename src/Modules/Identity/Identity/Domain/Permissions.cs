namespace Identity.Domain;

public static class Permissions
{
    public const string PatientsRegister = "Patients.Register";
    public const string PatientsRead = "Patients.Read";
    public const string AppointmentsCreate = "Appointments.Create";
    public const string AppointmentsRead = "Appointments.Read";
    public const string EncountersCreate = "Encounters.Create";
    public const string EncountersRead = "Encounters.Read";
    public const string EncountersUpdate = "Encounters.Update";
    public const string PrescriptionsWrite = "Prescriptions.Write";
    public const string PrescriptionsRead = "Prescriptions.Read";
    public const string PrescriptionsDispense = "Prescriptions.Dispense";
    public const string UsersManage = "Users.Manage";

    public static readonly string[] All =
    [
        PatientsRegister, PatientsRead,
        AppointmentsCreate, AppointmentsRead,
        EncountersCreate, EncountersRead, EncountersUpdate,
        PrescriptionsWrite, PrescriptionsRead, PrescriptionsDispense,
        UsersManage,
    ];
}
