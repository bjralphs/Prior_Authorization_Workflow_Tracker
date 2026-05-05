using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services;
using Prior_Authorization_Workflow_Tracker.Services.Models;
using Tests.Unit.Fakes;

namespace Tests.Unit.Services;

/// <summary>
/// Unit tests for PaRequestService (T13, T34 — ≥ 80% coverage target).
/// Covers: Draft creation, RequestNumber format, role-gating, comment visibility,
/// queue filtering, and hard delete (BR-014).
/// </summary>
public class PaRequestServiceTests
{
    // ── Builder ───────────────────────────────────────────────────────────────

    private static AppDbContext MakeDb(string name)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(opts);
    }

    private static PaRequestService BuildSvc(
        string dbName,
        FakeCurrentUserService? user = null,
        FakeDateTimeProvider? clock = null)
    {
        user  ??= FakeCurrentUserService.AsSpecialist();
        clock ??= new FakeDateTimeProvider();

        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => MakeDb(dbName));

        var ruleFactory = new Mock<IDbContextFactory<AppDbContext>>();
        ruleFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(() => MakeDb(dbName));

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var rules = new BusinessRuleService(ruleFactory.Object, clock);

        return new PaRequestService(
            factory.Object, user, clock, audit.Object, rules,
            NullLogger<PaRequestService>.Instance);
    }

    private static CreatePaRequestModel MinimalDraft() => new()
    {
        PatientMrn    = "MRN001",
        InsurancePlanID = 1,
        ProcedureCodeID = 1,
    };

    // ── CreateDraftAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDraft_GeneratesRequestNumberFromIdentityId()
    {
        var clock = new FakeDateTimeProvider { UtcNow = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc) };
        var svc = BuildSvc("rn_format", clock: clock);

        var result = await svc.CreateDraftAsync(MinimalDraft());

        // Format: PA-YYYY-NNNNN (ID padded to 5 digits)
        Assert.Matches(@"^PA-2026-\d{5}$", result.RequestNumber);
        Assert.Equal($"PA-2026-{result.ID:D5}", result.RequestNumber);
    }

    [Fact]
    public async Task CreateDraft_SetsDraftStatusAndCurrentUser()
    {
        var user = FakeCurrentUserService.AsSpecialist();
        user.UserId = "spec-abc";
        var svc = BuildSvc("draft_status", user: user);

        var result = await svc.CreateDraftAsync(MinimalDraft());

        Assert.Equal(PaStatus.Draft, result.Status);
        Assert.Equal("spec-abc", result.SubmittedByUserID);
    }

    [Fact]
    public async Task CreateDraft_MissingMrn_ThrowsBusinessRuleViolation()
    {
        var svc = BuildSvc("draft_no_mrn");
        var model = MinimalDraft();
        model.PatientMrn = "";

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.CreateDraftAsync(model));
    }

    [Fact]
    public async Task CreateDraft_ProviderRole_ThrowsAuthorizationException()
    {
        var provider = FakeCurrentUserService.AsProvider();
        var svc = BuildSvc("draft_provider_denied", user: provider);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.CreateDraftAsync(MinimalDraft()));
    }

    [Fact]
    public async Task CreateDraft_InactiveUser_ThrowsAuthorizationException()
    {
        var user = FakeCurrentUserService.AsSpecialist();
        user.IsActive = false;
        var svc = BuildSvc("draft_inactive", user: user);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.CreateDraftAsync(MinimalDraft()));
    }

    // ── GetQueueAsync RBAC ────────────────────────────────────────────────────

    [Fact]
    public async Task GetQueue_ProviderSeesOnlyOwnRequests()
    {
        var provider = FakeCurrentUserService.AsProvider();
        provider.UserId = "provider-abc";
        var svc = BuildSvc("queue_provider", user: provider);
        var db = MakeDb("queue_provider");

        // Request owned by this provider
        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "M1",
            PatientName = "P1", ProviderID = "provider-abc",
            SubmittedByUserID = "s1", Status = PaStatus.Submitted,
            InsurancePlanID = 1, ProcedureCodeID = 1,
        });
        // Request for a different provider
        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00002", PatientMrn = "M2",
            PatientName = "P2", ProviderID = "other-provider",
            SubmittedByUserID = "s1", Status = PaStatus.Submitted,
            InsurancePlanID = 1, ProcedureCodeID = 1,
        });
        await db.SaveChangesAsync();

        var (items, total) = await svc.GetQueueAsync(new PaRequestFilter());

        Assert.Equal(1, total);
        Assert.All(items, r => Assert.Equal("provider-abc", r.ProviderID));
    }

    [Fact]
    public async Task GetQueue_SpecialistSeesAllRequests()
    {
        var specialist = FakeCurrentUserService.AsSpecialist();
        var svc = BuildSvc("queue_specialist", user: specialist);
        var db = MakeDb("queue_specialist");

        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "M1",
            PatientName = "P1", ProviderID = "p1",
            SubmittedByUserID = "s1", Status = PaStatus.Submitted,
            InsurancePlanID = 1, ProcedureCodeID = 1,
        });
        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00002", PatientMrn = "M2",
            PatientName = "P2", ProviderID = "p2",
            SubmittedByUserID = "s2", Status = PaStatus.UnderReview,
            InsurancePlanID = 1, ProcedureCodeID = 1,
        });
        await db.SaveChangesAsync();

        var (items, total) = await svc.GetQueueAsync(new PaRequestFilter());

        Assert.Equal(2, total);
    }

    [Fact]
    public async Task GetQueue_StatusFilter_ReturnsOnlyMatchingStatus()
    {
        var svc = BuildSvc("queue_filter_status");
        var db = MakeDb("queue_filter_status");

        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "M1", PatientName = "P1",
            ProviderID = "p1", SubmittedByUserID = "s1",
            Status = PaStatus.Submitted, InsurancePlanID = 1, ProcedureCodeID = 1,
        });
        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00002", PatientMrn = "M2", PatientName = "P2",
            ProviderID = "p1", SubmittedByUserID = "s1",
            Status = PaStatus.Approved, InsurancePlanID = 1, ProcedureCodeID = 1,
        });
        await db.SaveChangesAsync();

        var (items, total) = await svc.GetQueueAsync(
            new PaRequestFilter { Status = PaStatus.Submitted });

        Assert.Equal(1, total);
        Assert.All(items, r => Assert.Equal(PaStatus.Submitted, r.Status));
    }

    // ── GetCommentsAsync role filtering ───────────────────────────────────────

    [Fact]
    public async Task GetQueue_PatientSearch_FiltersByNameOrMrn()
    {
        var svc = BuildSvc("queue_patient_search");
        var db = MakeDb("queue_patient_search");

        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "MRN-001",
            PatientName = "Alice Smith", ProviderID = "p1", SubmittedByUserID = "s1",
            Status = PaStatus.Submitted, InsurancePlanID = 1, ProcedureCodeID = 1,
        });
        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00002", PatientMrn = "MRN-002",
            PatientName = "Bob Jones", ProviderID = "p1", SubmittedByUserID = "s1",
            Status = PaStatus.Submitted, InsurancePlanID = 1, ProcedureCodeID = 1,
        });
        await db.SaveChangesAsync();

        // Null search returns all
        var (all, totalAll) = await svc.GetQueueAsync(new PaRequestFilter());
        Assert.Equal(2, totalAll);

        // Search by name fragment — use count as proxy (InMemory Contains works for CountAsync;
        // ToListAsync behavior with Contains + OrderBy is tested in integration tests against SQL Server)
        var (_, totalByName) = await svc.GetQueueAsync(
            new PaRequestFilter { PatientSearch = "Alice" });
        Assert.Equal(1, totalByName);

        // Search by MRN — count only
        var (_, totalByMrn) = await svc.GetQueueAsync(
            new PaRequestFilter { PatientSearch = "MRN-002" });
        Assert.Equal(1, totalByMrn);

        // Search with non-matching term returns 0
        var (_, totalNoMatch) = await svc.GetQueueAsync(
            new PaRequestFilter { PatientSearch = "ZZZNOMATCH" });
        Assert.Equal(0, totalNoMatch);
    }

    [Fact]
    public async Task GetComments_ProviderDoesNotSeeInternalComments()
    {
        var provider = FakeCurrentUserService.AsProvider();
        var svc = BuildSvc("comments_provider", user: provider);
        var db = MakeDb("comments_provider");

        db.PaComments.Add(new PaComment
        {
            PaRequestID = 1, CommentText = "Internal note.",
            IsInternalOnly = true, AuthoredByUserID = "spec",
            AuthoredAt = DateTime.UtcNow,
        });
        db.PaComments.Add(new PaComment
        {
            PaRequestID = 1, CommentText = "Visible to all.",
            IsInternalOnly = false, AuthoredByUserID = "spec",
            AuthoredAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var comments = await svc.GetCommentsAsync(requestId: 1);

        Assert.Single(comments);
        Assert.False(comments[0].IsInternalOnly);
    }

    [Fact]
    public async Task GetComments_SpecialistSeesInternalComments()
    {
        var svc = BuildSvc("comments_specialist");
        var db = MakeDb("comments_specialist");

        db.PaComments.Add(new PaComment
        {
            PaRequestID = 1, CommentText = "Internal note.",
            IsInternalOnly = true, AuthoredByUserID = "spec",
            AuthoredAt = DateTime.UtcNow,
        });
        db.PaComments.Add(new PaComment
        {
            PaRequestID = 1, CommentText = "External note.",
            IsInternalOnly = false, AuthoredByUserID = "spec",
            AuthoredAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var comments = await svc.GetCommentsAsync(requestId: 1);

        Assert.Equal(2, comments.Count);
    }

    // ── DeleteDraftAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteDraft_ByCreator_RemovesRequest()
    {
        var user = FakeCurrentUserService.AsSpecialist();
        user.UserId = "spec-del";
        var svc = BuildSvc("delete_creator", user: user);
        var db = MakeDb("delete_creator");

        db.PaRequests.Add(new PaRequest
        {
            ID = 1, RequestNumber = "PA-2026-00001", PatientMrn = "M1",
            PatientName = "P1", ProviderID = "p1", SubmittedByUserID = "spec-del",
            Status = PaStatus.Draft, InsurancePlanID = 1, ProcedureCodeID = 1,
        });
        await db.SaveChangesAsync();

        await svc.DeleteDraftAsync(1);

        // Verify the request is gone (hard delete bypasses soft-delete filter)
        var remaining = await db.PaRequests.IgnoreQueryFilters().CountAsync(r => r.ID == 1);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task DeleteDraft_ByOtherSpecialist_ThrowsBusinessRuleViolation()
    {
        var user = FakeCurrentUserService.AsSpecialist();
        user.UserId = "spec-other";
        var svc = BuildSvc("delete_other_spec", user: user);
        var db = MakeDb("delete_other_spec");

        db.PaRequests.Add(new PaRequest
        {
            ID = 1, RequestNumber = "PA-2026-00001", PatientMrn = "M1",
            PatientName = "P1", ProviderID = "p1", SubmittedByUserID = "spec-original",
            Status = PaStatus.Draft, InsurancePlanID = 1, ProcedureCodeID = 1,
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.DeleteDraftAsync(1));
        Assert.Equal("BR-014", ex.RuleId);
    }

    // ── Pagination ────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates TotalCount from GetQueueAsync — the count query runs before pagination
    /// and uses the same RBAC-filtered base query.
    /// </summary>
    [Fact]
    public async Task GetQueue_TotalCountMatchesSeedData()
    {
        var dbName = "queue_totalcount_" + Guid.NewGuid().ToString("N");
        var svc = BuildSvc(dbName);
        var db = MakeDb(dbName);

        for (var i = 1; i <= 5; i++)
        {
            db.PaRequests.Add(new PaRequest
            {
                RequestNumber = $"PA-2026-{i:D5}", PatientMrn = $"MRN{i:D3}",
                PatientName = $"Patient {i:D2}", ProviderID = "p1", SubmittedByUserID = "s1",
                Status = PaStatus.Submitted, InsurancePlanID = 1, ProcedureCodeID = 1,
            });
        }
        await db.SaveChangesAsync();

        var (_, total) = await svc.GetQueueAsync(new PaRequestFilter { PageSize = 100 });

        Assert.Equal(5, total);
    }

    // ── UpdateDraftAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDraft_UpdatesFieldsOnOwnDraft()
    {
        var dbName = "update_draft_own";
        var user = FakeCurrentUserService.AsSpecialist();
        user.UserId = "spec-owner";
        var svc = BuildSvc(dbName, user: user);
        var db  = MakeDb(dbName);

        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "OLD-MRN",
            InsurancePlanID = 1, ProcedureCodeID = 1,
            Status = PaStatus.Draft, SubmittedByUserID = "spec-owner",
            ClinicalJustification = "original",
        });
        await db.SaveChangesAsync();

        var req = await db.PaRequests.FirstAsync();
        var model = new CreatePaRequestModel
        {
            PatientMrn      = "NEW-MRN",
            InsurancePlanID = 1,
            ProcedureCodeID = 1,
        };

        await svc.UpdateDraftAsync(req.ID, model);

        await using var read = MakeDb(dbName);
        var updated = await read.PaRequests.IgnoreQueryFilters().FirstAsync();
        Assert.Equal("NEW-MRN", updated.PatientMrn);
    }

    [Fact]
    public async Task UpdateDraft_NonOwnerSpecialist_ThrowsAuthorizationException()
    {
        var dbName = "update_draft_nonowner";
        var owner   = FakeCurrentUserService.AsSpecialist();
        owner.UserId = "spec-owner";
        var nonOwner = FakeCurrentUserService.AsSpecialist();
        nonOwner.UserId = "spec-other";
        var svc = BuildSvc(dbName, user: nonOwner);
        var db  = MakeDb(dbName);

        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "MRN001",
            InsurancePlanID = 1, ProcedureCodeID = 1,
            Status = PaStatus.Draft, SubmittedByUserID = "spec-owner",
        });
        await db.SaveChangesAsync();

        var req = await db.PaRequests.FirstAsync();
        var model = new CreatePaRequestModel { PatientMrn = "NEW", InsurancePlanID = 1, ProcedureCodeID = 1 };

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.UpdateDraftAsync(req.ID, model));
    }

    [Fact]
    public async Task UpdateDraft_NonDraftStatus_ThrowsBusinessRuleViolation()
    {
        var dbName = "update_draft_badstatus";
        var user = FakeCurrentUserService.AsSpecialist();
        user.UserId = "spec-owner";
        var svc = BuildSvc(dbName, user: user);
        var db  = MakeDb(dbName);

        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "MRN001",
            InsurancePlanID = 1, ProcedureCodeID = 1,
            Status = PaStatus.UnderReview, SubmittedByUserID = "spec-owner",
        });
        await db.SaveChangesAsync();

        var req = await db.PaRequests.IgnoreQueryFilters().FirstAsync();
        var model = new CreatePaRequestModel { PatientMrn = "NEW", InsurancePlanID = 1, ProcedureCodeID = 1 };

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.UpdateDraftAsync(req.ID, model));
    }

    // ── AddCommentAsync RBAC ──────────────────────────────────────────────────

    [Fact]
    public async Task AddComment_Provider_CannotAddInternalComment()
    {
        var dbName   = "comment_provider_internal";
        var provider = FakeCurrentUserService.AsProvider();
        var svc      = BuildSvc(dbName, user: provider);
        var db       = MakeDb(dbName);

        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "MRN001",
            InsurancePlanID = 1, ProcedureCodeID = 1,
            Status = PaStatus.Submitted, SubmittedByUserID = "spec-id",
            ProviderID = provider.UserId,
        });
        await db.SaveChangesAsync();

        var req = await db.PaRequests.IgnoreQueryFilters().FirstAsync();

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.AddCommentAsync(req.ID, "My internal note.", isInternal: true));
    }

    [Fact]
    public async Task AddComment_Provider_CanAddPublicComment()
    {
        var dbName   = "comment_provider_public";
        var provider = FakeCurrentUserService.AsProvider();
        var svc      = BuildSvc(dbName, user: provider);
        var db       = MakeDb(dbName);

        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "MRN001",
            InsurancePlanID = 1, ProcedureCodeID = 1,
            Status = PaStatus.Submitted, SubmittedByUserID = "spec-id",
            ProviderID = provider.UserId,
        });
        await db.SaveChangesAsync();

        var req = await db.PaRequests.IgnoreQueryFilters().FirstAsync();

        var comment = await svc.AddCommentAsync(req.ID, "Patient needs this urgently.", isInternal: false);

        Assert.Equal(provider.UserId, comment.AuthoredByUserID);
        Assert.False(comment.IsInternalOnly);
    }

    [Fact]
    public async Task AddComment_Specialist_CanAddInternalComment()
    {
        var dbName     = "comment_specialist_internal";
        var specialist = FakeCurrentUserService.AsSpecialist();
        var svc        = BuildSvc(dbName, user: specialist);
        var db         = MakeDb(dbName);

        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "MRN001",
            InsurancePlanID = 1, ProcedureCodeID = 1,
            Status = PaStatus.Submitted, SubmittedByUserID = specialist.UserId,
        });
        await db.SaveChangesAsync();

        var req = await db.PaRequests.IgnoreQueryFilters().FirstAsync();

        var comment = await svc.AddCommentAsync(req.ID, "Internal note.", isInternal: true);

        Assert.True(comment.IsInternalOnly);
    }

    // ── T-C4: AddDocumentAsync / GetDocumentsAsync ────────────────────────────

    [Fact]
    public async Task AddDocumentAsync_Specialist_AttachesDocumentAndWritesAudit()
    {
        var dbName     = "doc_specialist_upload";
        var specialist = FakeCurrentUserService.AsSpecialist();
        var auditMock  = new Mock<IAuditService>();
        auditMock.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => MakeDb(dbName));
        var ruleFactory = new Mock<IDbContextFactory<AppDbContext>>();
        ruleFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(() => MakeDb(dbName));
        var clock = new FakeDateTimeProvider();
        var rules = new BusinessRuleService(ruleFactory.Object, clock);
        var svc   = new PaRequestService(factory.Object, specialist, clock,
                        auditMock.Object, rules, NullLogger<PaRequestService>.Instance);

        // Seed a request so the existence check passes
        var db = MakeDb(dbName);
        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "MRN001",
            InsurancePlanID = 1, ProcedureCodeID = 1,
            Status = PaStatus.Submitted, SubmittedByUserID = specialist.UserId,
        });
        await db.SaveChangesAsync();
        var reqId = (await db.PaRequests.IgnoreQueryFilters().FirstAsync()).ID;

        var doc = await svc.AddDocumentAsync(
            reqId, "ClinicalNote_2026.pdf", "ClinicalNote", "application/pdf", 102_400);

        Assert.Equal(reqId, doc.PaRequestID);
        Assert.Equal("ClinicalNote_2026.pdf", doc.FileName);
        Assert.Equal("ClinicalNote", doc.DocumentType);
        Assert.Equal("application/pdf", doc.ContentType);
        Assert.Equal(102_400, doc.FileSizeBytes);
        Assert.Equal(specialist.UserId, doc.UploadedByUserID);

        // Verify audit was called with "Uploaded"
        auditMock.Verify(a => a.LogAsync(
            "PaDocument", It.IsAny<string>(), "Uploaded",
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddDocumentAsync_BillingManager_ThrowsAuthorizationException()
    {
        // Billing Manager does NOT have upload permission (§10.1)
        var dbName  = "doc_billing_rejected";
        var billing = FakeCurrentUserService.AsBillingManager();
        var svc     = BuildSvc(dbName, user: billing);
        var db      = MakeDb(dbName);
        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "MRN001",
            InsurancePlanID = 1, ProcedureCodeID = 1,
            Status = PaStatus.Submitted, SubmittedByUserID = "some-specialist",
        });
        await db.SaveChangesAsync();
        var reqId = (await db.PaRequests.IgnoreQueryFilters().FirstAsync()).ID;

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.AddDocumentAsync(reqId, "report.pdf", "LabResult", "application/pdf", 0));
    }

    [Fact]
    public async Task GetDocumentsAsync_ReturnsDocsOrderedByUploadedAt()
    {
        var dbName     = "doc_get_ordered";
        var specialist = FakeCurrentUserService.AsSpecialist();
        var svc        = BuildSvc(dbName, user: specialist);
        var db         = MakeDb(dbName);

        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "MRN001",
            InsurancePlanID = 1, ProcedureCodeID = 1,
            Status = PaStatus.Submitted, SubmittedByUserID = specialist.UserId,
        });
        await db.SaveChangesAsync();
        var reqId = (await db.PaRequests.IgnoreQueryFilters().FirstAsync()).ID;

        var t0 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        db.PaDocuments.AddRange(
            new PaDocument { PaRequestID = reqId, FileName = "first.pdf",  DocumentType = "ClinicalNote",
                             ContentType = "application/pdf", UploadedByUserID = specialist.UserId, UploadedAt = t0 },
            new PaDocument { PaRequestID = reqId, FileName = "second.pdf", DocumentType = "LabResult",
                             ContentType = "application/pdf", UploadedByUserID = specialist.UserId, UploadedAt = t0.AddHours(1) }
        );
        await db.SaveChangesAsync();

        var docs = await svc.GetDocumentsAsync(reqId);

        Assert.Equal(2, docs.Count);
        Assert.Equal("first.pdf",  docs[0].FileName);
        Assert.Equal("second.pdf", docs[1].FileName);
    }

    [Fact]
    public async Task AddDocumentAsync_EmptyFileName_ThrowsBusinessRuleViolation()
    {
        var dbName     = "doc_empty_name";
        var specialist = FakeCurrentUserService.AsSpecialist();
        var svc        = BuildSvc(dbName, user: specialist);
        var db         = MakeDb(dbName);
        db.PaRequests.Add(new PaRequest
        {
            RequestNumber = "PA-2026-00001", PatientMrn = "MRN001",
            InsurancePlanID = 1, ProcedureCodeID = 1,
            Status = PaStatus.Submitted, SubmittedByUserID = specialist.UserId,
        });
        await db.SaveChangesAsync();
        var reqId = (await db.PaRequests.IgnoreQueryFilters().FirstAsync()).ID;

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.AddDocumentAsync(reqId, "   ", "ClinicalNote", "application/pdf", 0));
    }
}

