using FluentValidation;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;
using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Validation;

/// <summary>
/// FluentValidation validator for approval decision fields (§8.5).
/// Covers BR-006, BR-011, BR-012.
/// BR-007 (granted ≤ requested) is enforced in BusinessRuleService because it
/// requires the parent PaRequest.ApprovedUnitsRequested for the comparison.
/// </summary>
public sealed class ApprovalDecisionValidator : AbstractValidator<ApprovalDecisionModel>
{
    public ApprovalDecisionValidator(IDateTimeProvider clock)
    {
        // BR-012: Granted must be ≥ 1
        RuleFor(d => d.ApprovedUnitsGranted)
            .GreaterThanOrEqualTo(1)
            .WithMessage("BR-012: Approved units granted must be at least 1.");

        // BR-011: Start date must be today or future (evaluated against injected clock)
        var today = clock.UtcNow.Date;
        RuleFor(d => d.AuthorizationStartDate)
            .Must(date => date.Date >= today)
            .WithMessage("BR-011: Authorization start date must be today or a future date.");

        // BR-006: End date must be after start date
        RuleFor(d => d.AuthorizationEndDate)
            .Must((model, endDate) => endDate > model.AuthorizationStartDate)
            .WithMessage("BR-006: Authorization end date must be after the authorization start date.");
    }
}
