namespace Prior_Authorization_Workflow_Tracker.Constants;

/// <summary>
/// Role name constants matching the ASP.NET Core Identity roles seeded in DbSeeder (§4, §10).
/// Use these throughout the service and UI layers instead of hard-coded string literals.
/// </summary>
public static class Roles
{
    public const string Specialist     = "Authorization Specialist";
    public const string Provider       = "Treating Provider";
    public const string BillingManager = "Billing Manager";
    public const string Reviewer       = "Payer Reviewer";
    public const string Admin          = "Administrator";
}
