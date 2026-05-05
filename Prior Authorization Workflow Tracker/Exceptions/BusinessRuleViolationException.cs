namespace Prior_Authorization_Workflow_Tracker.Exceptions;

/// <summary>
/// Thrown by the service layer when a BR-xxx business rule is violated (§9, §17.1).
/// Carries the rule ID and a user-facing plain-language description.
/// </summary>
public class BusinessRuleViolationException : PaException
{
    /// <summary>Business rule identifier, e.g. "BR-005".</summary>
    public string RuleId { get; }

    public BusinessRuleViolationException(string ruleId, string message)
        : base(message)
    {
        RuleId = ruleId;
    }
}
