using System.Text.RegularExpressions;
using FluentValidation;
using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Validation;

/// <summary>
/// FluentValidation validator for PA request submission fields (FR-001, §9.1).
/// Covers pure field-level rules only:
///   BR-003 — ICD-10 diagnosis code format
///   BR-004 — Units requested 1–999
///   BR-010 — Clinical justification ≥ 50 characters
///   Required field checks (PatientMrn, PatientName, ProviderID)
///
/// DB-dependent rules (BR-001 procedure RequiresPriorAuth, BR-005 duplicate active request)
/// are enforced in BusinessRuleService because they require a DB query.
/// </summary>
public sealed class PaRequestSubmitValidator : AbstractValidator<PaRequest>
{
    // Compiled regex per §9.1 BR-003. Timeout prevents ReDoS (OWASP Top 10 A03).
    private static readonly Regex Icd10Regex = new(
        @"^[A-Z][0-9]{2}(\.[0-9A-Z]{1,4})?$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    public PaRequestSubmitValidator()
    {
        RuleFor(r => r.PatientMrn)
            .NotEmpty().WithMessage("Patient MRN is required.");

        RuleFor(r => r.PatientName)
            .NotEmpty().WithMessage("Patient name is required.");

        RuleFor(r => r.ProviderID)
            .NotEmpty().WithMessage("Provider is required.");

        // BR-003
        RuleFor(r => r.DiagnosisCode)
            .NotEmpty().WithMessage("Diagnosis code is required.")
            .Matches(Icd10Regex)
            .WithMessage("BR-003: Diagnosis code must be a valid ICD-10 format (e.g. M17.11, G35, C34.10).");

        // BR-004
        RuleFor(r => r.ApprovedUnitsRequested)
            .InclusiveBetween(1, 999)
            .WithMessage("BR-004: Approved units requested must be between 1 and 999.");

        // BR-010
        RuleFor(r => r.ClinicalJustification)
            .NotEmpty().WithMessage("Clinical justification is required.")
            .MinimumLength(50)
            .WithMessage("BR-010: Clinical justification must be at least 50 characters.");
    }
}
