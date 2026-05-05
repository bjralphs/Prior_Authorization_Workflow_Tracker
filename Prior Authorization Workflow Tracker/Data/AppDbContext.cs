using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Data;

/// <summary>
/// EF Core database context.
///
/// Design decisions:
///  - Extends IdentityDbContext&lt;ApplicationUser&gt; to co-locate Identity tables.
///  - Global HasQueryFilter on PaRequests (IsDeleted) prevents soft-deleted rows
///    from leaking into any query (BUG-003 mitigation).
///  - RowVersion on PaRequests is configured as IsRowVersion() so EF Core issues
///    the correct WHERE clause on UPDATE and throws DbUpdateConcurrencyException
///    on conflicts (BUG-002 mitigation).
///  - All enums stored as strings for readability in the database and migration diffs.
///  - Indexes follow §7.3 of the PRD.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<PaRequest> PaRequests => Set<PaRequest>();
    public DbSet<PaStatusHistory> PaStatusHistories => Set<PaStatusHistory>();
    public DbSet<PaDocument> PaDocuments => Set<PaDocument>();
    public DbSet<PaComment> PaComments => Set<PaComment>();
    public DbSet<InsurancePlan> InsurancePlans => Set<InsurancePlan>();
    public DbSet<ProcedureCode> ProcedureCodes => Set<ProcedureCode>();
    public DbSet<DenialReason> DenialReasons => Set<DenialReason>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── PaRequest ─────────────────────────────────────────────────────────

        builder.Entity<PaRequest>(e =>
        {
            // Soft-delete global filter (BUG-003 mitigation)
            e.HasQueryFilter(r => !r.IsDeleted);

            // Optimistic concurrency token (BUG-002 mitigation)
            e.Property(r => r.RowVersion)
                .IsRowVersion();

            // Enum → string conversions
            e.Property(r => r.Status)
                .HasConversion<string>()
                .HasMaxLength(50);

            e.Property(r => r.Priority)
                .HasConversion<string>()
                .HasMaxLength(20);

            // Column lengths
            e.Property(r => r.RequestNumber).HasMaxLength(20);
            e.Property(r => r.PatientMrn).HasMaxLength(50);
            e.Property(r => r.PatientName).HasMaxLength(200);
            e.Property(r => r.DiagnosisCode).HasMaxLength(10);
            e.Property(r => r.AuthorizationNumber).HasMaxLength(50);
            // UX_PaRequests_AuthorizationNumber: unique partial index (BUG-01 safety net).
            // Filter excludes NULLs so draft/submitted requests do not conflict.
            e.HasIndex(r => r.AuthorizationNumber)
                .IsUnique()
                .HasFilter("[AuthorizationNumber] IS NOT NULL")
                .HasDatabaseName("UX_PaRequests_AuthorizationNumber");

            // Indexes (§7.3 + IDX-001/IDX-002 from ROADMAP_v4)
            e.HasIndex(r => r.Status).HasDatabaseName("IX_PaRequests_Status");
            e.HasIndex(r => r.PatientMrn).HasDatabaseName("IX_PaRequests_PatientMrn");
            e.HasIndex(r => r.DecisionDueDate).HasDatabaseName("IX_PaRequests_DecisionDueDate");
            e.HasIndex(r => r.SubmittedByUserID).HasDatabaseName("IX_PaRequests_SubmittedByUserID");
            // Reviewer and provider filters appear in queue and dashboards — indexed for NFR-001.
            e.HasIndex(r => r.ReviewerUserID).HasDatabaseName("IX_PaRequests_ReviewerUserID");
            e.HasIndex(r => r.ProviderID).HasDatabaseName("IX_PaRequests_ProviderID");

            // FK relationships — explicit to avoid EF default cascade-delete surprises
            e.HasOne(r => r.InsurancePlan)
                .WithMany(p => p.PaRequests)
                .HasForeignKey(r => r.InsurancePlanID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.ProcedureCode)
                .WithMany(pc => pc.PaRequests)
                .HasForeignKey(r => r.ProcedureCodeID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.DenialReason)
                .WithMany(dr => dr.PaRequests)
                .HasForeignKey(r => r.DenialReasonID)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // Multiple FKs to ApplicationUser — must name each one explicitly
            e.HasOne(r => r.SubmittedByUser)
                .WithMany(u => u.SubmittedRequests)
                .HasForeignKey(r => r.SubmittedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.Provider)
                .WithMany(u => u.ProviderRequests)
                .HasForeignKey(r => r.ProviderID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.ReviewerUser)
                .WithMany(u => u.ReviewedRequests)
                .HasForeignKey(r => r.ReviewerUserID)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PaStatusHistory ───────────────────────────────────────────────────

        builder.Entity<PaStatusHistory>(e =>
        {
            e.Property(h => h.FromStatus)
                .HasConversion<string>()
                .HasMaxLength(50);

            e.Property(h => h.ToStatus)
                .HasConversion<string>()
                .HasMaxLength(50);

            e.Property(h => h.ChangeReason).HasMaxLength(500);

            e.HasIndex(h => h.PaRequestID)
                .HasDatabaseName("IX_PaStatusHistory_PaRequestID");

            e.HasOne(h => h.PaRequest)
                .WithMany(r => r.StatusHistory)
                .HasForeignKey(h => h.PaRequestID)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(h => h.ChangedByUser)
                .WithMany(u => u.StatusChanges)
                .HasForeignKey(h => h.ChangedByUserID)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PaDocument ────────────────────────────────────────────────────────

        builder.Entity<PaDocument>(e =>
        {
            e.Property(d => d.FileName).HasMaxLength(500);
            e.Property(d => d.ContentType).HasMaxLength(100);
            e.Property(d => d.StoragePath).HasMaxLength(1000);
            e.Property(d => d.DocumentType).HasMaxLength(100);

            e.HasOne(d => d.PaRequest)
                .WithMany(r => r.Documents)
                .HasForeignKey(d => d.PaRequestID)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(d => d.UploadedByUser)
                .WithMany(u => u.UploadedDocuments)
                .HasForeignKey(d => d.UploadedByUserID)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PaComment ─────────────────────────────────────────────────────────

        builder.Entity<PaComment>(e =>
        {
            e.HasOne(c => c.PaRequest)
                .WithMany(r => r.Comments)
                .HasForeignKey(c => c.PaRequestID)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.AuthoredByUser)
                .WithMany(u => u.AuthoredComments)
                .HasForeignKey(c => c.AuthoredByUserID)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── AuditLog ──────────────────────────────────────────────────────────

        builder.Entity<AuditLog>(e =>
        {
            e.Property(a => a.EntityName).HasMaxLength(100);
            e.Property(a => a.EntityID).HasMaxLength(100);
            e.Property(a => a.Action).HasMaxLength(100);
            e.Property(a => a.UserID).HasMaxLength(450);
            e.Property(a => a.UserName).HasMaxLength(200);
            e.Property(a => a.IpAddress).HasMaxLength(45);
            e.Property(a => a.CorrelationId).HasMaxLength(50);

            // Indexes (§7.3)
            e.HasIndex(a => a.OccurredAt)
                .HasDatabaseName("IX_AuditLogs_OccurredAt");

            e.HasIndex(a => new { a.EntityID, a.EntityName })
                .HasDatabaseName("IX_AuditLogs_EntityID_EntityName");
        });

        // ── Notification ──────────────────────────────────────────────────────

        builder.Entity<Notification>(e =>
        {
            e.Property(n => n.NotificationType)
                .HasConversion<string>()
                .HasMaxLength(50);

            e.Property(n => n.Message).HasMaxLength(500);

            e.HasIndex(n => new { n.UserID, n.IsRead })
                .HasDatabaseName("IX_Notifications_UserId_IsRead");

            e.HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserID)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(n => n.PaRequest)
                .WithMany(r => r.Notifications)
                .HasForeignKey(n => n.PaRequestID)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── InsurancePlan ─────────────────────────────────────────────────────

        builder.Entity<InsurancePlan>(e =>
        {
            e.Property(p => p.PlanName).HasMaxLength(200);
            e.Property(p => p.PayerName).HasMaxLength(200);
            e.Property(p => p.PlanType).HasMaxLength(50);
            e.Property(p => p.PhoneNumber).HasMaxLength(20);
            e.Property(p => p.FaxNumber).HasMaxLength(20);
        });

        // ── ProcedureCode ─────────────────────────────────────────────────────

        builder.Entity<ProcedureCode>(e =>
        {
            e.Property(pc => pc.Code).HasMaxLength(10);
            e.Property(pc => pc.Description).HasMaxLength(500);
        });

        // ── DenialReason ──────────────────────────────────────────────────────

        builder.Entity<DenialReason>(e =>
        {
            e.Property(dr => dr.Code).HasMaxLength(20);
            e.Property(dr => dr.Description).HasMaxLength(500);
        });

        // ── ApplicationUser extensions ────────────────────────────────────────

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.FullName).HasMaxLength(200);
            e.Property(u => u.Department).HasMaxLength(100);
        });
    }
}
