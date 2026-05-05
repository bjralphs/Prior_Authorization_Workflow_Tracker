using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prior_Authorization_Workflow_Tracker.Migrations
{
    /// <summary>
    /// Creates four reporting SQL views as specified in PRD §7.2.
    ///
    /// All views are read-only aggregations over existing tables; no schema changes
    /// to underlying tables are made.  Each view is idempotent (CREATE OR ALTER) so
    /// re-running the migration is safe.
    ///
    /// Views:
    ///   vw_PaRequestSummary          — flat joined row per request
    ///   vw_DenialsByReason           — denial counts by reason
    ///   vw_RequestVolumeByMonth      — submission counts by year-month
    ///   vw_ExpiringAuthorizations    — approved auths expiring within 30 days
    /// </summary>
    public partial class AddReportingViews : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── vw_PaRequestSummary ───────────────────────────────────────────
            // Joins PaRequests, ProcedureCodes, InsurancePlans,
            // and AspNetUsers (provider + reviewer + submitter).
            migrationBuilder.Sql(@"
CREATE OR ALTER VIEW [dbo].[vw_PaRequestSummary] AS
SELECT
    r.ID                      AS RequestId,
    r.RequestNumber,
    r.PatientMrn,
    r.PatientName,
    r.PatientDob,
    r.Status,
    r.Priority,
    r.SubmittedAt,
    r.DecisionDueDate,
    r.DecisionRenderedAt,
    r.AuthorizationNumber,
    r.AuthorizationStartDate,
    r.AuthorizationEndDate,
    r.ApprovedUnitsRequested,
    r.ApprovedUnitsGranted,
    r.DiagnosisCode,
    r.DenialNotes,
    r.IsDeleted,
    r.CreatedAt,
    r.UpdatedAt,
    pc.Code                   AS ProcedureCode,
    pc.Description            AS ProcedureDescription,
    ip.PlanName               AS InsurancePlanName,
    ip.PayerName,
    ip.PlanType,
    provider.FullName         AS ProviderName,
    provider.Email            AS ProviderEmail,
    submitter.FullName        AS SubmittedByName,
    submitter.Email           AS SubmittedByEmail,
    reviewer.FullName         AS ReviewerName,
    reviewer.Email            AS ReviewerEmail,
    dr.Code                   AS DenialReasonCode,
    dr.Description            AS DenialReasonDescription,
    dr.IsAppealable           AS DenialIsAppealable
FROM dbo.PaRequests r
    INNER JOIN dbo.ProcedureCodes   pc       ON pc.ID       = r.ProcedureCodeID
    INNER JOIN dbo.InsurancePlans   ip       ON ip.ID       = r.InsurancePlanID
    LEFT  JOIN dbo.AspNetUsers      provider ON provider.Id = r.ProviderID
    LEFT  JOIN dbo.AspNetUsers      submitter ON submitter.Id = r.SubmittedByUserID
    LEFT  JOIN dbo.AspNetUsers      reviewer  ON reviewer.Id  = r.ReviewerUserID
    LEFT  JOIN dbo.DenialReasons    dr        ON dr.ID        = r.DenialReasonID
WHERE r.IsDeleted = 0;
");

            // ── vw_DenialsByReason ────────────────────────────────────────────
            // Aggregates denied request counts and percentages per denial reason.
            migrationBuilder.Sql(@"
CREATE OR ALTER VIEW [dbo].[vw_DenialsByReason] AS
SELECT
    dr.ID                                       AS DenialReasonId,
    dr.Code                                     AS DenialReasonCode,
    dr.Description                              AS DenialReasonDescription,
    dr.IsAppealable,
    COUNT(r.ID)                                 AS DenialCount,
    CAST(COUNT(r.ID) AS FLOAT)
        / NULLIF((SELECT COUNT(*) FROM dbo.PaRequests
                  WHERE Status = 'Denied' AND IsDeleted = 0), 0) * 100.0
                                                AS DenialPct
FROM dbo.DenialReasons dr
    LEFT JOIN dbo.PaRequests r
        ON r.DenialReasonID = dr.ID
        AND r.Status = 'Denied'
        AND r.IsDeleted = 0
GROUP BY
    dr.ID, dr.Code, dr.Description, dr.IsAppealable;
");

            // ── vw_RequestVolumeByMonth ───────────────────────────────────────
            // Counts submissions grouped by year-month and status breakdown.
            migrationBuilder.Sql(@"
CREATE OR ALTER VIEW [dbo].[vw_RequestVolumeByMonth] AS
SELECT
    YEAR(SubmittedAt)   AS SubmissionYear,
    MONTH(SubmittedAt)  AS SubmissionMonth,
    Status,
    COUNT(*)            AS RequestCount
FROM dbo.PaRequests
WHERE SubmittedAt IS NOT NULL
  AND IsDeleted   = 0
GROUP BY
    YEAR(SubmittedAt),
    MONTH(SubmittedAt),
    Status;
");

            // ── vw_ExpiringAuthorizations ─────────────────────────────────────
            // Approved requests where AuthorizationEndDate falls within 30 days.
            // The 30-day window matches the dashboard alert threshold (PRD §8.6).
            migrationBuilder.Sql(@"
CREATE OR ALTER VIEW [dbo].[vw_ExpiringAuthorizations] AS
SELECT
    r.ID                    AS RequestId,
    r.RequestNumber,
    r.PatientMrn,
    r.PatientName,
    r.AuthorizationNumber,
    r.AuthorizationStartDate,
    r.AuthorizationEndDate,
    DATEDIFF(DAY, GETUTCDATE(), r.AuthorizationEndDate)
                            AS DaysUntilExpiry,
    provider.FullName       AS ProviderName,
    provider.Email          AS ProviderEmail,
    submitter.FullName      AS SubmittedByName,
    submitter.Email         AS SubmittedByEmail
FROM dbo.PaRequests r
    LEFT  JOIN dbo.AspNetUsers provider  ON provider.Id  = r.ProviderID
    LEFT  JOIN dbo.AspNetUsers submitter ON submitter.Id = r.SubmittedByUserID
WHERE r.Status     = 'Approved'
  AND r.IsDeleted  = 0
  AND r.AuthorizationEndDate IS NOT NULL
  AND r.AuthorizationEndDate >= GETUTCDATE()
  AND r.AuthorizationEndDate <= DATEADD(DAY, 30, GETUTCDATE());
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS [dbo].[vw_ExpiringAuthorizations];");
            migrationBuilder.Sql("DROP VIEW IF EXISTS [dbo].[vw_RequestVolumeByMonth];");
            migrationBuilder.Sql("DROP VIEW IF EXISTS [dbo].[vw_DenialsByReason];");
            migrationBuilder.Sql("DROP VIEW IF EXISTS [dbo].[vw_PaRequestSummary];");
        }
    }
}
