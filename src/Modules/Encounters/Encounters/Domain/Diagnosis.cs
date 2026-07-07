namespace Encounters.Domain;

/// <summary>
/// ICD-10 diagnosis attached to an encounter. Owned entity — lives in the encounter_diagnoses table.
/// Created only through Encounter.AddDiagnosis(); never constructed externally.
/// </summary>
public sealed class Diagnosis
{
    private Diagnosis() { } // required by EF Core

    internal Diagnosis(string icd10Code, string description, DiagnosisType type)
    {
        Icd10Code = icd10Code;
        Description = description;
        Type = type;
    }

    public string Icd10Code { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DiagnosisType Type { get; private set; }
}
