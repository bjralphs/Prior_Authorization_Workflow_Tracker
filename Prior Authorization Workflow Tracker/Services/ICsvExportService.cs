using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Generates CSV exports of PA request data for authorized roles (§8.10, FR-010).
/// Only Billing Manager and Admin roles may call this service.
/// </summary>
public interface ICsvExportService
{
    /// <summary>
    /// Exports PA requests matching the specified filter to a UTF-8 BOM CSV byte array.
    /// File name follows the format <c>PA_Export_YYYYMMDD_HHmmss.csv</c>.
    /// Only callable by Billing Manager and Admin roles (enforced at service layer).
    /// </summary>
    Task<(byte[] Data, string FileName)> ExportRequestsAsync(
        PaRequestFilter filter, CancellationToken ct = default);
}
