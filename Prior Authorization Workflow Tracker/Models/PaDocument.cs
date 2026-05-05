namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>
/// Document metadata attached to a PA request (§7.1).
/// In v1 no file bytes are persisted; StoragePath is metadata only (known limitation §18).
/// </summary>
public class PaDocument
{
    public int ID { get; set; }

    public int PaRequestID { get; set; }

    /// <summary>e.g. "ClinicalNote" | "LabResult" | "ImageReport"</summary>
    public string DocumentType { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME type, e.g. "application/pdf"; required for correct HTTP download headers.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Stored at upload time; used to enforce size limits at the service layer.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>File system or blob path (metadata only in v1).</summary>
    public string StoragePath { get; set; } = string.Empty;

    public string UploadedByUserID { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public PaRequest PaRequest { get; set; } = null!;
    public ApplicationUser UploadedByUser { get; set; } = null!;
}
