using Core;

namespace Encounters.Domain;

public sealed class Encounter : AuditableEntity
{
    private List<Diagnosis> _diagnoses = [];

    private Encounter() { } // required by EF Core

    public Guid AppointmentId { get; private set; }
    public Guid PatientId { get; private set; }
    public EncounterStatus Status { get; private set; }
    public string? ClinicalNotes { get; private set; }
    public VitalSigns? Vitals { get; private set; }
    public IReadOnlyList<Diagnosis> Diagnoses => _diagnoses.AsReadOnly();
    public DateTimeOffset? ClosedAt { get; private set; }

    public static Encounter Open(Guid appointmentId, Guid patientId, string? clinicalNotes = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            PatientId = patientId,
            Status = EncounterStatus.Open,
            ClinicalNotes = clinicalNotes,
        };

    public Result AddDiagnosis(string icd10Code, string description, DiagnosisType diagnosisType)
    {
        if (Status != EncounterStatus.Open)
            return Result.Fail(new Error("Encounter.Closed",
                "Cannot add a diagnosis to a closed encounter."));

        _diagnoses.Add(new Diagnosis(icd10Code, description, diagnosisType));
        return Result.Ok();
    }

    public Result RecordVitals(VitalSigns vitals)
    {
        if (Status != EncounterStatus.Open)
            return Result.Fail(new Error("Encounter.Closed",
                "Cannot record vitals for a closed encounter."));

        Vitals = vitals;
        return Result.Ok();
    }

    public Result Close(string? clinicalNotes, DateTimeOffset now)
    {
        if (Status != EncounterStatus.Open)
            return Result.Fail(new Error("Encounter.InvalidStatus",
                $"Cannot close an encounter with status '{Status}'."));

        if (clinicalNotes is not null)
            ClinicalNotes = clinicalNotes;

        Status = EncounterStatus.Closed;
        ClosedAt = now;
        return Result.Ok();
    }
}
