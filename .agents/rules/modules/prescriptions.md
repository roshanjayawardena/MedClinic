# Module: Prescriptions

Owns the full prescription lifecycle — the doctor writes the script during/after an Encounter, and the
pharmacist (who also handles front desk) reads and dispenses it.

Read `phi-and-tenancy.md`, `auditing.md`, and `eventing.md` before working here.

---

## Responsibility

| In scope | Out of scope |
|---|---|
| Prescription creation (Doctor) | Clinical diagnosis (Encounters) |
| Item-level drug/dosage/instructions | Drug inventory / stock management |
| Dispense recording (Pharmacist) | Patient allergies (owned by Patients) |
| Prescription status lifecycle | Billing (billing module reacts to dispense) |

---

## Who does what

| Action | Role | Permission |
|---|---|---|
| Create prescription | Doctor | `Prescriptions.Create` |
| Read prescription detail | Doctor, Pharmacist | `Prescriptions.Read` |
| Dispense prescription | Pharmacist | `Prescriptions.Dispense` |
| Void prescription | Doctor | `Prescriptions.Void` |

---

## Business rules (non-negotiable)

1. **No Prescription without a closed Encounter.** Validate `EncounterExistsQuery` + `Status == Closed`.
2. **Allergy check.** Before creating a prescription, query `PatientAllergiesQuery` (Patients.Contracts)
   and return `Result<T>.Failure(...)` if any item conflicts with a recorded allergy. Log the allergy-check
   outcome (conflict or clear) — never the allergy details themselves.
3. **Doctor creates; Pharmacist dispenses.** No role may perform both actions on the same prescription.
4. **Dispensed = immutable.** No changes after `Dispense()` is called. Corrections require a new prescription.
5. **Audit every access** — same rules as Encounters (see `auditing.md`).

---

## Prescription aggregate — lifecycle

```
Draft → Active → Dispensed
             ↘ Voided
```

- `Draft`: created, items can be added/removed.
- `Active`: locked by the doctor signing off (future: digital signature or simple confirm).
  In v1, the pharmacist creates directly in `Active` state after verifying the encounter.
- `Dispensed`: pharmacist confirms physical handover. Records `DispensedAt`, `DispensedBy`.
- `Voided`: cancelled before dispensing (e.g., patient didn't collect, allergy discovered).

---

## Layout

```
src/Modules/Prescriptions/
├── Prescriptions/
│   ├── PrescriptionsModule.cs
│   ├── Domain/
│   │   ├── Prescription.cs
│   │   ├── PrescriptionItem.cs   (owned entity)
│   │   └── PrescriptionStatus.cs
│   ├── Persistence/
│   │   ├── PrescriptionsDbContext.cs
│   │   └── Configurations/
│   │       └── PrescriptionConfiguration.cs
│   └── Features/
│       ├── CreatePrescription/
│       ├── AddPrescriptionItem/
│       ├── DispensePrescription/
│       ├── VoidPrescription/
│       └── GetPrescription/      (AUDITED read)
└── Prescriptions.Contracts/
    ├── CreatePrescription.cs
    ├── DispensePrescription.cs
    └── Events/
        └── PrescriptionDispensedIntegrationEvent.cs
```

---

## PHI mapping

| Field | PHI? | Safe to log? |
|---|---|---|
| `Prescription.Id` | No | Yes |
| `Prescription.PatientId` | No | Yes |
| `PrescriptionItem.DrugName` | **YES** | Never (reveals condition) |
| `PrescriptionItem.Dosage` | **YES** | Never |
| `PrescriptionItem.Instructions` | **YES** | Never |

---

## Module-specific gotchas

- **Never log drug names or dosages** — they are PHI as they imply a diagnosis.
- **Allergy conflict must surface as a business failure**, not a warning. Return `Result.Fail` with
  a non-PHI message (e.g., "Prescription blocked: allergy conflict detected for PatientId {id}").
- **The allergy details themselves** (which drug, what reaction) must NOT appear in the failure message
  returned to the API — they go in the internal allergy-check log entry only if absolutely required,
  and even then, only by drug code, never name.
