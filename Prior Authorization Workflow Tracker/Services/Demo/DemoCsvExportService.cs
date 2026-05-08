using CsvHelper;
using CsvHelper.Configuration;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;
using Prior_Authorization_Workflow_Tracker.Services.Models;
using System.Globalization;
using System.Text;

namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>Generates CSV from the in-memory store using CsvHelper (same as production).</summary>
public sealed class DemoCsvExportService : ICsvExportService
{
    private readonly DemoDataStore       _store;
    private readonly ICurrentUserService _currentUser;
    private readonly IPaRequestService   _requests;

    public DemoCsvExportService(DemoDataStore store, ICurrentUserService currentUser, IPaRequestService requests)
    {
        _store       = store;
        _currentUser = currentUser;
        _requests    = requests;
    }

    public async Task<(byte[] Data, string FileName)> ExportRequestsAsync(
        PaRequestFilter filter, CancellationToken ct = default)
    {
        // Export all matching (skip paging for exports)
        filter.Page     = 1;
        filter.PageSize = int.MaxValue;
        var (items, _)  = await _requests.GetQueueAsync(filter, ct);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
        };

        using var ms = new MemoryStream();
        // UTF-8 BOM so Excel opens without encoding issues
        ms.Write(Encoding.UTF8.GetPreamble());

        using (var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true))
        using (var csv = new CsvWriter(writer, config))
        {
            csv.WriteHeader<CsvRow>();
            await csv.NextRecordAsync();
            foreach (var r in items)
            {
                csv.WriteRecord(ToCsvRow(r));
                await csv.NextRecordAsync();
            }
        }

        var fileName = $"PA_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        return (ms.ToArray(), fileName);
    }

    private CsvRow ToCsvRow(PaRequest r) => new()
    {
        RequestNumber          = r.RequestNumber,
        PatientName            = r.PatientName,
        PatientMrn             = r.PatientMrn,
        PatientDob             = r.PatientDob.ToString("yyyy-MM-dd"),
        Status                 = r.Status.ToString(),
        Priority               = r.Priority.ToString(),
        InsurancePlan          = r.InsurancePlan?.PlanName ?? r.InsurancePlanID.ToString(),
        ProcedureCode          = r.ProcedureCode?.Code ?? r.ProcedureCodeID.ToString(),
        DiagnosisCode          = r.DiagnosisCode,
        SubmittedAt            = r.SubmittedAt?.ToString("yyyy-MM-dd") ?? string.Empty,
        DecisionDueDate        = r.DecisionDueDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        DecisionRenderedAt     = r.DecisionRenderedAt?.ToString("yyyy-MM-dd") ?? string.Empty,
        UnitsRequested         = r.ApprovedUnitsRequested.ToString(),
        UnitsGranted           = r.ApprovedUnitsGranted?.ToString() ?? string.Empty,
        AuthorizationNumber    = r.AuthorizationNumber ?? string.Empty,
        AuthorizationStartDate = r.AuthorizationStartDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        AuthorizationEndDate   = r.AuthorizationEndDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        DenialReason           = r.DenialReason?.Code ?? string.Empty,
    };

    private sealed class CsvRow
    {
        public string RequestNumber          { get; set; } = string.Empty;
        public string PatientName            { get; set; } = string.Empty;
        public string PatientMrn             { get; set; } = string.Empty;
        public string PatientDob             { get; set; } = string.Empty;
        public string Status                 { get; set; } = string.Empty;
        public string Priority               { get; set; } = string.Empty;
        public string InsurancePlan          { get; set; } = string.Empty;
        public string ProcedureCode          { get; set; } = string.Empty;
        public string DiagnosisCode          { get; set; } = string.Empty;
        public string SubmittedAt            { get; set; } = string.Empty;
        public string DecisionDueDate        { get; set; } = string.Empty;
        public string DecisionRenderedAt     { get; set; } = string.Empty;
        public string UnitsRequested         { get; set; } = string.Empty;
        public string UnitsGranted           { get; set; } = string.Empty;
        public string AuthorizationNumber    { get; set; } = string.Empty;
        public string AuthorizationStartDate { get; set; } = string.Empty;
        public string AuthorizationEndDate   { get; set; } = string.Empty;
        public string DenialReason           { get; set; } = string.Empty;
    }
}
