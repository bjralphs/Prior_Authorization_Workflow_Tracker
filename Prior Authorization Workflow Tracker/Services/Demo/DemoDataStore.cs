using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>
/// Singleton in-memory store for the GitHub Pages demo.
/// Pre-seeded with realistic PA requests spanning all statuses, multiple roles,
/// and several months of history. All data resets on browser refresh.
/// Thread-safe via lock for the rare case of concurrent WASM re-renders.
/// </summary>
public sealed class DemoDataStore
{
    private int _nextRequestId = 100;
    private int _nextCommentId = 200;
    private int _nextDocId = 300;
    private int _nextNotifId = 400;
    private int _nextAuditId = 500;
    private int _nextHistoryId = 600;

    private readonly object _lock = new();

    // ── Lookup tables (immutable after construction) ──────────────────────────
    public IReadOnlyList<InsurancePlan> InsurancePlans { get; }
    public IReadOnlyList<ProcedureCode> ProcedureCodes { get; }
    public IReadOnlyList<DenialReason> DenialReasons { get; }

    // ── Demo users ────────────────────────────────────────────────────────────
    public IReadOnlyList<DemoUser> Users { get; }

    // ── Mutable collections ───────────────────────────────────────────────────
    public List<PaRequest>       Requests     { get; } = [];
    public List<PaStatusHistory> StatusHistory { get; } = [];
    public List<PaComment>       Comments     { get; } = [];
    public List<PaDocument>      Documents    { get; } = [];
    public List<Notification>    Notifications { get; } = [];
    public List<AuditLog>        AuditLogs    { get; } = [];

    public DemoDataStore()
    {
        InsurancePlans = BuildInsurancePlans();
        ProcedureCodes = BuildProcedureCodes();
        DenialReasons  = BuildDenialReasons();
        Users          = BuildUsers();
        SeedRequests();
        SeedNotifications();
        SeedAuditLogs();
    }

    // ── ID generators (thread-safe) ───────────────────────────────────────────
    public int NextRequestId()  { lock (_lock) return ++_nextRequestId; }
    public int NextCommentId()  { lock (_lock) return ++_nextCommentId; }
    public int NextDocId()      { lock (_lock) return ++_nextDocId; }
    public int NextNotifId()    { lock (_lock) return ++_nextNotifId; }
    public int NextAuditId()    { lock (_lock) return ++_nextAuditId; }
    public int NextHistoryId()  { lock (_lock) return ++_nextHistoryId; }

    // ── Insurance Plans ───────────────────────────────────────────────────────
    private static List<InsurancePlan> BuildInsurancePlans() =>
    [
        new() { ID=1, PlanName="BlueCross Premier PPO",  PayerName="BlueCross BlueShield", PlanType="PPO",      RoutineDecisionDays=14, UrgentDecisionDays=3, EmergentDecisionDays=1, PhoneNumber="800-555-0100", IsActive=true },
        new() { ID=2, PlanName="Aetna HMO Select",       PayerName="Aetna",                PlanType="HMO",      RoutineDecisionDays=10, UrgentDecisionDays=2, EmergentDecisionDays=1, PhoneNumber="800-555-0200", IsActive=true },
        new() { ID=3, PlanName="United Choice Plus",     PayerName="UnitedHealthcare",     PlanType="PPO",      RoutineDecisionDays=14, UrgentDecisionDays=3, EmergentDecisionDays=1, PhoneNumber="800-555-0300", IsActive=true },
        new() { ID=4, PlanName="Cigna Open Access",      PayerName="Cigna",                PlanType="PPO",      RoutineDecisionDays=14, UrgentDecisionDays=3, EmergentDecisionDays=1, PhoneNumber="800-555-0400", IsActive=true },
        new() { ID=5, PlanName="Medicare Part B",        PayerName="CMS",                  PlanType="Medicare", RoutineDecisionDays=14, UrgentDecisionDays=3, EmergentDecisionDays=1, PhoneNumber="800-555-0500", IsActive=true },
        new() { ID=6, PlanName="Medicaid Managed Care",  PayerName="Molina Healthcare",    PlanType="Medicaid", RoutineDecisionDays=10, UrgentDecisionDays=2, EmergentDecisionDays=1, PhoneNumber="800-555-0600", IsActive=true },
    ];

    // ── Procedure Codes ───────────────────────────────────────────────────────
    private static List<ProcedureCode> BuildProcedureCodes() =>
    [
        new() { ID=1, Code="27447", Description="Total Knee Arthroplasty",         RequiresPriorAuth=true },
        new() { ID=2, Code="27130", Description="Total Hip Arthroplasty",           RequiresPriorAuth=true },
        new() { ID=3, Code="70553", Description="MRI Brain with Contrast",          RequiresPriorAuth=true },
        new() { ID=4, Code="93306", Description="Echocardiography Complete",        RequiresPriorAuth=false },
        new() { ID=5, Code="43239", Description="Upper GI Endoscopy with Biopsy",   RequiresPriorAuth=true },
        new() { ID=6, Code="27245", Description="ORIF Femoral Neck Fracture",       RequiresPriorAuth=true },
        new() { ID=7, Code="70450", Description="CT Head/Brain without Contrast",   RequiresPriorAuth=true },
        new() { ID=8, Code="64483", Description="Epidural Steroid Injection",       RequiresPriorAuth=true },
        new() { ID=9, Code="90837", Description="Psychotherapy 60 min",             RequiresPriorAuth=true },
        new() { ID=10, Code="29827", Description="Arthroscopic Rotator Cuff Repair",RequiresPriorAuth=true },
    ];

    // ── Denial Reasons ────────────────────────────────────────────────────────
    private static List<DenialReason> BuildDenialReasons() =>
    [
        new() { ID=1, Code="MED-NEC", Description="Not medically necessary based on clinical guidelines", IsAppealable=true,  AppealDeadlineDays=30 },
        new() { ID=2, Code="EXP-BEN", Description="Service not covered under current benefit plan",       IsAppealable=true,  AppealDeadlineDays=30 },
        new() { ID=3, Code="DUP-SVC", Description="Duplicate service already authorized",                  IsAppealable=false, AppealDeadlineDays=0  },
        new() { ID=4, Code="STEP-TX", Description="Step therapy required — try conservative treatment first",IsAppealable=true, AppealDeadlineDays=30 },
        new() { ID=5, Code="MISS-INFO",Description="Missing or incomplete clinical information",            IsAppealable=true,  AppealDeadlineDays=14 },
        new() { ID=6, Code="NON-PART",Description="Provider not in network for requested service",         IsAppealable=true,  AppealDeadlineDays=30 },
    ];

    // ── Demo Users ────────────────────────────────────────────────────────────
    private static List<DemoUser> BuildUsers() =>
    [
        new() { Id="user-spec-1",  FullName="Sarah Chen",       Email="specialist@pademo.com",  Role=Constants.Roles.Specialist,    Department="Authorization", IsActive=true },
        new() { Id="user-spec-2",  FullName="Marcus Webb",      Email="specialist2@pademo.com", Role=Constants.Roles.Specialist,    Department="Authorization", IsActive=true },
        new() { Id="user-prov-1",  FullName="Dr. Elena Vasquez",Email="provider@pademo.com",    Role=Constants.Roles.Provider,      Department="Orthopedics",   IsActive=true },
        new() { Id="user-prov-2",  FullName="Dr. James Kim",    Email="provider2@pademo.com",   Role=Constants.Roles.Provider,      Department="Neurology",     IsActive=true },
        new() { Id="user-prov-3",  FullName="Dr. Aisha Patel",  Email="provider3@pademo.com",   Role=Constants.Roles.Provider,      Department="Gastroenterology",IsActive=true },
        new() { Id="user-rev-1",   FullName="Robert Torres",    Email="reviewer@pademo.com",    Role=Constants.Roles.Reviewer,      Department="Payer Review",  IsActive=true },
        new() { Id="user-bill-1",  FullName="Linda Park",       Email="billing@pademo.com",     Role=Constants.Roles.BillingManager,Department="Billing",       IsActive=true },
        new() { Id="user-admin-1", FullName="Admin User",       Email="admin@pademo.com",       Role=Constants.Roles.Admin,         Department="IT",            IsActive=true },
    ];

    // ── Seed PA Requests ──────────────────────────────────────────────────────
    private void SeedRequests()
    {
        var now = DateTime.UtcNow;
        var plan1 = InsurancePlans[0]; // BlueCross PPO
        var plan2 = InsurancePlans[1]; // Aetna HMO
        var plan3 = InsurancePlans[2]; // United
        var plan4 = InsurancePlans[3]; // Cigna
        var plan5 = InsurancePlans[4]; // Medicare
        var plan6 = InsurancePlans[5]; // Medicaid

        var proc1 = ProcedureCodes[0];  // Knee
        var proc2 = ProcedureCodes[1];  // Hip
        var proc3 = ProcedureCodes[2];  // MRI Brain
        var proc5 = ProcedureCodes[4];  // Endoscopy
        var proc6 = ProcedureCodes[5];  // ORIF
        var proc7 = ProcedureCodes[6];  // CT Head
        var proc8 = ProcedureCodes[7];  // Epidural
        var proc9 = ProcedureCodes[8];  // Psychotherapy
        var proc10= ProcedureCodes[9];  // Rotator Cuff

        var spec1  = "user-spec-1";
        var spec2  = "user-spec-2";
        var prov1  = "user-prov-1";
        var prov2  = "user-prov-2";
        var prov3  = "user-prov-3";
        var rev1   = "user-rev-1";

        int id = 1;
        string Num(int i) => $"PA-2026-{i:D5}";

        // 1. Approved — knee replacement
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10001", PatientName = "Dorothy Harrington", PatientDob = new DateTime(1948, 3, 15),
            InsurancePlanID = plan1.ID, InsurancePlan = plan1,
            ProviderID = prov1, ProcedureCodeID = proc1.ID, ProcedureCode = proc1,
            DiagnosisCode = "M17.11", ClinicalJustification = "Patient presents with severe osteoarthritis of the right knee, grade IV on X-ray. Conservative treatment including NSAIDs and physical therapy for 6 months has failed. Patient has significant functional impairment. Total knee arthroplasty is medically necessary.",
            Status = PaStatus.Approved, Priority = PaPriority.Routine,
            SubmittedByUserID = spec1, ReviewerUserID = rev1,
            SubmittedAt = now.AddDays(-18), DecisionDueDate = now.AddDays(-4), DecisionRenderedAt = now.AddDays(-5),
            ApprovedUnitsRequested = 1, ApprovedUnitsGranted = 1,
            AuthorizationNumber = "AUTH-20260420-00001",
            AuthorizationStartDate = now.AddDays(10), AuthorizationEndDate = now.AddDays(100),
            CreatedAt = now.AddDays(-20), UpdatedAt = now.AddDays(-5),
        }, plan1, prov1, spec1, rev1);

        // 2. Denied — MRI Brain
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10002", PatientName = "Gerald Fontaine", PatientDob = new DateTime(1961, 7, 22),
            InsurancePlanID = plan2.ID, InsurancePlan = plan2,
            ProviderID = prov2, ProcedureCodeID = proc3.ID, ProcedureCode = proc3,
            DiagnosisCode = "G43.909", ClinicalJustification = "Patient reports chronic migraines with recent increase in severity. Rule out secondary causes. MRI brain with contrast requested to evaluate for structural pathology.",
            Status = PaStatus.Denied, Priority = PaPriority.Routine,
            SubmittedByUserID = spec1, ReviewerUserID = rev1,
            SubmittedAt = now.AddDays(-15), DecisionDueDate = now.AddDays(-5), DecisionRenderedAt = now.AddDays(-6),
            ApprovedUnitsRequested = 1,
            DenialReasonID = DenialReasons[3].ID, DenialReason = DenialReasons[3],
            DenialNotes = "Step therapy required. Per plan guidelines, conservative management with medication therapy must be documented for minimum 3 months before advanced imaging is authorized.",
            CreatedAt = now.AddDays(-16), UpdatedAt = now.AddDays(-6),
        }, plan2, prov2, spec1, rev1);

        // 3. UnderReview — Hip Replacement
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10003", PatientName = "Miriam Okafor", PatientDob = new DateTime(1955, 11, 8),
            InsurancePlanID = plan3.ID, InsurancePlan = plan3,
            ProviderID = prov1, ProcedureCodeID = proc2.ID, ProcedureCode = proc2,
            DiagnosisCode = "M16.11", ClinicalJustification = "Patient has end-stage osteoarthritis of the left hip confirmed by radiograph showing complete loss of joint space. Patient has failed conservative treatment over 12 months. ADLs significantly impaired. Total hip arthroplasty indicated.",
            Status = PaStatus.UnderReview, Priority = PaPriority.Urgent,
            SubmittedByUserID = spec2, ReviewerUserID = rev1,
            SubmittedAt = now.AddDays(-4), DecisionDueDate = now.AddDays(-1), DecisionRenderedAt = null,
            ApprovedUnitsRequested = 1,
            CreatedAt = now.AddDays(-5), UpdatedAt = now.AddDays(-4),
        }, plan3, prov1, spec2, rev1);

        // 4. Submitted — Endoscopy
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10004", PatientName = "Thomas Bradfield", PatientDob = new DateTime(1972, 4, 30),
            InsurancePlanID = plan4.ID, InsurancePlan = plan4,
            ProviderID = prov3, ProcedureCodeID = proc5.ID, ProcedureCode = proc5,
            DiagnosisCode = "K92.1", ClinicalJustification = "Patient presents with recurrent melena and iron deficiency anemia refractory to oral supplementation. Upper GI endoscopy with biopsy requested to evaluate for peptic ulcer disease and H. pylori infection.",
            Status = PaStatus.Submitted, Priority = PaPriority.Urgent,
            SubmittedByUserID = spec1,
            SubmittedAt = now.AddDays(-2), DecisionDueDate = now.AddDays(1),
            ApprovedUnitsRequested = 1,
            CreatedAt = now.AddDays(-3), UpdatedAt = now.AddDays(-2),
        }, plan4, prov3, spec1, null);

        // 5. PendingInfo — CT Head
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10005", PatientName = "Angela Rossi", PatientDob = new DateTime(1980, 9, 12),
            InsurancePlanID = plan5.ID, InsurancePlan = plan5,
            ProviderID = prov2, ProcedureCodeID = proc7.ID, ProcedureCode = proc7,
            DiagnosisCode = "R51.9", ClinicalJustification = "Patient presents with new-onset severe headache. CT head ordered to rule out intracranial hemorrhage.",
            Status = PaStatus.PendingInfo, Priority = PaPriority.Emergent,
            SubmittedByUserID = spec2, ReviewerUserID = rev1,
            SubmittedAt = now.AddDays(-3), DecisionDueDate = now.AddDays(0),
            ApprovedUnitsRequested = 1,
            CreatedAt = now.AddDays(-4), UpdatedAt = now.AddDays(-1),
        }, plan5, prov2, spec2, rev1);

        // 6. Draft — Rotator Cuff
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10006", PatientName = "Victor Nguyen", PatientDob = new DateTime(1968, 2, 14),
            InsurancePlanID = plan1.ID, InsurancePlan = plan1,
            ProviderID = prov1, ProcedureCodeID = proc10.ID, ProcedureCode = proc10,
            DiagnosisCode = "M75.121", ClinicalJustification = "Patient has complete rotator cuff tear confirmed by MRI. Physical therapy 8 weeks failed. Surgical repair recommended.",
            Status = PaStatus.Draft, Priority = PaPriority.Routine,
            SubmittedByUserID = spec1,
            ApprovedUnitsRequested = 1,
            CreatedAt = now.AddDays(-1), UpdatedAt = now.AddDays(-1),
        }, plan1, prov1, spec1, null);

        // 7. Appealed — Hip (denied, now appealed)
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10007", PatientName = "Frances Delacroix", PatientDob = new DateTime(1943, 6, 5),
            InsurancePlanID = plan6.ID, InsurancePlan = plan6,
            ProviderID = prov1, ProcedureCodeID = proc2.ID, ProcedureCode = proc2,
            DiagnosisCode = "M16.12", ClinicalJustification = "Patient has bilateral hip OA with severe pain and functional limitation. Prior auth denied citing step therapy. Patient completed 6 months PT with no improvement. Appeal submitted with updated clinical records.",
            Status = PaStatus.Appealed, Priority = PaPriority.Routine,
            SubmittedByUserID = spec2, ReviewerUserID = rev1,
            SubmittedAt = now.AddDays(-30), DecisionDueDate = now.AddDays(-16), DecisionRenderedAt = now.AddDays(-17),
            ApprovedUnitsRequested = 1,
            DenialReasonID = DenialReasons[0].ID, DenialReason = DenialReasons[0],
            DenialNotes = "Medical necessity criteria not met per current clinical guidelines.",
            CreatedAt = now.AddDays(-32), UpdatedAt = now.AddDays(-10),
        }, plan6, prov1, spec2, rev1);

        // 8. Expired — old approval past end date
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10008", PatientName = "Harold Simmons", PatientDob = new DateTime(1952, 1, 19),
            InsurancePlanID = plan2.ID, InsurancePlan = plan2,
            ProviderID = prov1, ProcedureCodeID = proc1.ID, ProcedureCode = proc1,
            DiagnosisCode = "M17.12", ClinicalJustification = "Left knee OA with complete loss of cartilage. TKA medically necessary.",
            Status = PaStatus.Expired, Priority = PaPriority.Routine,
            SubmittedByUserID = spec1, ReviewerUserID = rev1,
            SubmittedAt = now.AddDays(-90), DecisionDueDate = now.AddDays(-76), DecisionRenderedAt = now.AddDays(-77),
            ApprovedUnitsRequested = 1, ApprovedUnitsGranted = 1,
            AuthorizationNumber = "AUTH-20260207-00002",
            AuthorizationStartDate = now.AddDays(-77), AuthorizationEndDate = now.AddDays(-17),
            CreatedAt = now.AddDays(-92), UpdatedAt = now.AddDays(-17),
        }, plan2, prov1, spec1, rev1);

        // 9. Approved — Epidural Steroid
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10009", PatientName = "Constance Bloom", PatientDob = new DateTime(1965, 8, 27),
            InsurancePlanID = plan3.ID, InsurancePlan = plan3,
            ProviderID = prov2, ProcedureCodeID = proc8.ID, ProcedureCode = proc8,
            DiagnosisCode = "M54.41", ClinicalJustification = "Patient with lumbar disc herniation causing radiculopathy. 6 weeks of physical therapy completed without adequate relief. ESI indicated for pain management.",
            Status = PaStatus.Approved, Priority = PaPriority.Routine,
            SubmittedByUserID = spec2, ReviewerUserID = rev1,
            SubmittedAt = now.AddDays(-10), DecisionDueDate = now.AddDays(4), DecisionRenderedAt = now.AddDays(-2),
            ApprovedUnitsRequested = 3, ApprovedUnitsGranted = 3,
            AuthorizationNumber = "AUTH-20260428-00003",
            AuthorizationStartDate = now.AddDays(5), AuthorizationEndDate = now.AddDays(95),
            CreatedAt = now.AddDays(-11), UpdatedAt = now.AddDays(-2),
        }, plan3, prov2, spec2, rev1);

        // 10. Submitted — Psychotherapy (overdue)
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10010", PatientName = "Nathan Cole", PatientDob = new DateTime(1990, 12, 3),
            InsurancePlanID = plan4.ID, InsurancePlan = plan4,
            ProviderID = prov3, ProcedureCodeID = proc9.ID, ProcedureCode = proc9,
            DiagnosisCode = "F33.1", ClinicalJustification = "Patient diagnosed with major depressive disorder, moderate severity. Requesting authorization for 16 sessions of individual psychotherapy over 4 months.",
            Status = PaStatus.Submitted, Priority = PaPriority.Routine,
            SubmittedByUserID = spec1,
            SubmittedAt = now.AddDays(-20), DecisionDueDate = now.AddDays(-6),
            ApprovedUnitsRequested = 16,
            CreatedAt = now.AddDays(-21), UpdatedAt = now.AddDays(-20),
        }, plan4, prov3, spec1, null);

        // 11. UnderReview — Knee (urgent, provider prov2)
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10011", PatientName = "Patricia Morse", PatientDob = new DateTime(1958, 5, 17),
            InsurancePlanID = plan5.ID, InsurancePlan = plan5,
            ProviderID = prov2, ProcedureCodeID = proc1.ID, ProcedureCode = proc1,
            DiagnosisCode = "M17.31", ClinicalJustification = "Patient with post-traumatic arthritis after meniscectomy 20 years ago. Severe pain and inability to ambulate. Bilateral knee replacement needed; requesting right knee first.",
            Status = PaStatus.UnderReview, Priority = PaPriority.Urgent,
            SubmittedByUserID = spec1, ReviewerUserID = rev1,
            SubmittedAt = now.AddDays(-5), DecisionDueDate = now.AddDays(-2), DecisionRenderedAt = null,
            ApprovedUnitsRequested = 1,
            CreatedAt = now.AddDays(-6), UpdatedAt = now.AddDays(-5),
        }, plan5, prov2, spec1, rev1);

        // 12. Approved — expiring soon (7 days)
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10012", PatientName = "Sylvia Hayward", PatientDob = new DateTime(1949, 10, 4),
            InsurancePlanID = plan1.ID, InsurancePlan = plan1,
            ProviderID = prov1, ProcedureCodeID = proc6.ID, ProcedureCode = proc6,
            DiagnosisCode = "S72.001A", ClinicalJustification = "Acute femoral neck fracture in elderly patient. ORIF indicated emergently.",
            Status = PaStatus.Approved, Priority = PaPriority.Emergent,
            SubmittedByUserID = spec2, ReviewerUserID = rev1,
            SubmittedAt = now.AddDays(-60), DecisionDueDate = now.AddDays(-59), DecisionRenderedAt = now.AddDays(-59),
            ApprovedUnitsRequested = 1, ApprovedUnitsGranted = 1,
            AuthorizationNumber = "AUTH-20260309-00004",
            AuthorizationStartDate = now.AddDays(-59), AuthorizationEndDate = now.AddDays(7),
            CreatedAt = now.AddDays(-61), UpdatedAt = now.AddDays(-59),
        }, plan1, prov1, spec2, rev1);

        // 13. Draft — Endoscopy (prov3)
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10013", PatientName = "Bernard Atchison", PatientDob = new DateTime(1975, 3, 22),
            InsurancePlanID = plan6.ID, InsurancePlan = plan6,
            ProviderID = prov3, ProcedureCodeID = proc5.ID, ProcedureCode = proc5,
            DiagnosisCode = "K57.30", ClinicalJustification = "Recurrent diverticulitis with suspected complicated diverticular disease. Colonoscopy to evaluate extent.",
            Status = PaStatus.Draft, Priority = PaPriority.Routine,
            SubmittedByUserID = spec2,
            ApprovedUnitsRequested = 1,
            CreatedAt = now.AddDays(-2), UpdatedAt = now.AddDays(-2),
        }, plan6, prov3, spec2, null);

        // 14. Denied — Psychotherapy (not covered)
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10014", PatientName = "Leah Tran", PatientDob = new DateTime(1995, 7, 9),
            InsurancePlanID = plan2.ID, InsurancePlan = plan2,
            ProviderID = prov3, ProcedureCodeID = proc9.ID, ProcedureCode = proc9,
            DiagnosisCode = "F41.1", ClinicalJustification = "Generalized anxiety disorder requiring psychotherapy. Patient unable to tolerate pharmacotherapy. 12 sessions requested.",
            Status = PaStatus.Denied, Priority = PaPriority.Routine,
            SubmittedByUserID = spec1, ReviewerUserID = rev1,
            SubmittedAt = now.AddDays(-25), DecisionDueDate = now.AddDays(-15), DecisionRenderedAt = now.AddDays(-16),
            ApprovedUnitsRequested = 12,
            DenialReasonID = DenialReasons[1].ID, DenialReason = DenialReasons[1],
            DenialNotes = "Psychotherapy benefit exhausted for the current plan year.",
            CreatedAt = now.AddDays(-26), UpdatedAt = now.AddDays(-16),
        }, plan2, prov3, spec1, rev1);

        // 15. Submitted — ORIF (emergency, overdue)
        AddRequest(new PaRequest
        {
            ID = id, RequestNumber = Num(id++),
            PatientMrn = "MRN-10015", PatientName = "Walter Grimes", PatientDob = new DateTime(1939, 12, 31),
            InsurancePlanID = plan3.ID, InsurancePlan = plan3,
            ProviderID = prov1, ProcedureCodeID = proc6.ID, ProcedureCode = proc6,
            DiagnosisCode = "S72.002A", ClinicalJustification = "81-year-old patient with displaced femoral neck fracture following fall. Emergency ORIF required to restore function and prevent complications.",
            Status = PaStatus.Submitted, Priority = PaPriority.Emergent,
            SubmittedByUserID = spec2,
            SubmittedAt = now.AddDays(-3), DecisionDueDate = now.AddDays(-2),
            ApprovedUnitsRequested = 1,
            CreatedAt = now.AddDays(-3), UpdatedAt = now.AddDays(-3),
        }, plan3, prov1, spec2, null);

        // Seed a few comments
        Comments.Add(new PaComment { ID = NextCommentId(), PaRequestID = 3, AuthoredByUserID = rev1, CommentText = "Additional clinical documentation requested — please provide PT records.", IsInternalOnly = false, AuthoredAt = now.AddDays(-3) });
        Comments.Add(new PaComment { ID = NextCommentId(), PaRequestID = 3, AuthoredByUserID = spec2, CommentText = "PT records uploaded. Patient completed 8-week course with no functional improvement.", IsInternalOnly = false, AuthoredAt = now.AddDays(-2) });
        Comments.Add(new PaComment { ID = NextCommentId(), PaRequestID = 5, AuthoredByUserID = rev1, CommentText = "Please provide recent neurology consult note and headache diary.", IsInternalOnly = false, AuthoredAt = now.AddDays(-1) });
        Comments.Add(new PaComment { ID = NextCommentId(), PaRequestID = 7, AuthoredByUserID = spec2, CommentText = "Appeal submitted with updated PT notes, pain scores, and functional assessment.", IsInternalOnly = false, AuthoredAt = now.AddDays(-10) });
        Comments.Add(new PaComment { ID = NextCommentId(), PaRequestID = 1, AuthoredByUserID = rev1, CommentText = "Approved. All criteria met. Authorization number generated.", IsInternalOnly = true, AuthoredAt = now.AddDays(-5) });

        // Seed some documents
        Documents.Add(new PaDocument { ID = NextDocId(), PaRequestID = 1, FileName = "Knee_Xray_Report.pdf", StoragePath = "/demo/doc", FileSizeBytes = 1248000, ContentType = "application/pdf", UploadedByUserID = spec1, UploadedAt = now.AddDays(-19) });
        Documents.Add(new PaDocument { ID = NextDocId(), PaRequestID = 3, FileName = "Hip_MRI_Report.pdf", StoragePath = "/demo/doc", FileSizeBytes = 2100000, ContentType = "application/pdf", UploadedByUserID = spec2, UploadedAt = now.AddDays(-4) });
        Documents.Add(new PaDocument { ID = NextDocId(), PaRequestID = 3, FileName = "PT_Progress_Notes_8wk.pdf", StoragePath = "/demo/doc", FileSizeBytes = 890000, ContentType = "application/pdf", UploadedByUserID = spec2, UploadedAt = now.AddDays(-2) });
        Documents.Add(new PaDocument { ID = NextDocId(), PaRequestID = 9, FileName = "ESI_Order_Form.pdf", StoragePath = "/demo/doc", FileSizeBytes = 456000, ContentType = "application/pdf", UploadedByUserID = spec2, UploadedAt = now.AddDays(-10) });
    }

    private void AddRequest(PaRequest r, InsurancePlan plan, string provId, string subId, string? revId)
    {
        lock (_lock)
        {
            Requests.Add(r);

            // Status history
            StatusHistory.Add(new PaStatusHistory
            {
                ID = NextHistoryId(), PaRequestID = r.ID,
                FromStatus = PaStatus.Draft, ToStatus = PaStatus.Draft,
                ChangedByUserID = subId, ChangedAt = r.CreatedAt,
                ChangeReason = "Request created"
            });

            if (r.Status != PaStatus.Draft && r.SubmittedAt.HasValue)
            {
                StatusHistory.Add(new PaStatusHistory
                {
                    ID = NextHistoryId(), PaRequestID = r.ID,
                    FromStatus = PaStatus.Draft, ToStatus = PaStatus.Submitted,
                    ChangedByUserID = subId, ChangedAt = r.SubmittedAt.Value,
                    ChangeReason = "Submitted for review"
                });
            }

            if (r.Status is PaStatus.UnderReview or PaStatus.Approved or PaStatus.Denied or PaStatus.Appealed or PaStatus.Expired)
            {
                StatusHistory.Add(new PaStatusHistory
                {
                    ID = NextHistoryId(), PaRequestID = r.ID,
                    FromStatus = PaStatus.Submitted, ToStatus = PaStatus.UnderReview,
                    ChangedByUserID = revId ?? subId, ChangedAt = r.SubmittedAt!.Value.AddHours(2),
                    ChangeReason = "Reviewer assigned, review initiated"
                });
            }

            if (r.Status == PaStatus.PendingInfo && r.ReviewerUserID != null)
            {
                StatusHistory.Add(new PaStatusHistory
                {
                    ID = NextHistoryId(), PaRequestID = r.ID,
                    FromStatus = PaStatus.Submitted, ToStatus = PaStatus.PendingInfo,
                    ChangedByUserID = revId ?? subId, ChangedAt = r.UpdatedAt,
                    ChangeReason = "Additional information requested"
                });
            }

            if (r.Status == PaStatus.Approved && r.DecisionRenderedAt.HasValue)
            {
                StatusHistory.Add(new PaStatusHistory
                {
                    ID = NextHistoryId(), PaRequestID = r.ID,
                    FromStatus = PaStatus.UnderReview, ToStatus = PaStatus.Approved,
                    ChangedByUserID = revId ?? subId, ChangedAt = r.DecisionRenderedAt.Value,
                    ChangeReason = $"Approved. Auth# {r.AuthorizationNumber}"
                });
            }

            if (r.Status == PaStatus.Denied && r.DecisionRenderedAt.HasValue)
            {
                StatusHistory.Add(new PaStatusHistory
                {
                    ID = NextHistoryId(), PaRequestID = r.ID,
                    FromStatus = PaStatus.UnderReview, ToStatus = PaStatus.Denied,
                    ChangedByUserID = revId ?? subId, ChangedAt = r.DecisionRenderedAt.Value,
                    ChangeReason = r.DenialNotes ?? "Denied"
                });
            }

            if (r.Status == PaStatus.Appealed)
            {
                StatusHistory.Add(new PaStatusHistory
                {
                    ID = NextHistoryId(), PaRequestID = r.ID,
                    FromStatus = PaStatus.Denied, ToStatus = PaStatus.Appealed,
                    ChangedByUserID = subId, ChangedAt = r.UpdatedAt,
                    ChangeReason = "Appeal filed by specialist"
                });
            }

            if (r.Status == PaStatus.Expired)
            {
                StatusHistory.Add(new PaStatusHistory
                {
                    ID = NextHistoryId(), PaRequestID = r.ID,
                    FromStatus = PaStatus.Approved, ToStatus = PaStatus.Expired,
                    ChangedByUserID = "system", ChangedAt = r.AuthorizationEndDate!.Value.AddDays(1),
                    ChangeReason = "Authorization expired (auto)"
                });
            }
        }
    }

    // ── Seed Notifications ────────────────────────────────────────────────────
    private void SeedNotifications()
    {
        var now = DateTime.UtcNow;
        Notifications.AddRange([
            new Notification { ID=NextNotifId(), UserID="user-spec-1", PaRequestID=1, Message="PA-2026-00001 for Dorothy Harrington has been Approved.", NotificationType=NotificationType.StatusChanged,               IsRead=true,  CreatedAt =now.AddDays(-5),  ReadAt=now.AddDays(-4) },
            new Notification { ID=NextNotifId(), UserID="user-spec-1", PaRequestID=2, Message="PA-2026-00002 for Gerald Fontaine has been Denied.",     NotificationType=NotificationType.StatusChanged,                 IsRead=true,  CreatedAt =now.AddDays(-6),  ReadAt=now.AddDays(-5) },
            new Notification { ID=NextNotifId(), UserID="user-spec-2", PaRequestID=5, Message="PA-2026-00005 for Angela Rossi requires additional information.", NotificationType=NotificationType.StatusChanged, IsRead=false, CreatedAt =now.AddDays(-1) },
            new Notification { ID=NextNotifId(), UserID="user-spec-1", PaRequestID=9, Message="PA-2026-00009 for Constance Bloom has been Approved.",   NotificationType=NotificationType.StatusChanged,               IsRead=false, CreatedAt =now.AddDays(-2) },
            new Notification { ID=NextNotifId(), UserID="user-spec-2", PaRequestID=7, Message="PA-2026-00007 for Frances Delacroix has been Denied.",   NotificationType=NotificationType.StatusChanged,                 IsRead=true,  CreatedAt =now.AddDays(-17), ReadAt=now.AddDays(-16) },
            new Notification { ID=NextNotifId(), UserID="user-prov-1", PaRequestID=1, Message="Authorization AUTH-20260420-00001 approved for Dorothy Harrington.", NotificationType=NotificationType.StatusChanged,   IsRead=true,  CreatedAt =now.AddDays(-5),  ReadAt=now.AddDays(-4) },
            new Notification { ID=NextNotifId(), UserID="user-prov-2", PaRequestID=9, Message="Authorization AUTH-20260428-00003 approved for Constance Bloom.",   NotificationType=NotificationType.StatusChanged,   IsRead=false, CreatedAt =now.AddDays(-2) },
            new Notification { ID=NextNotifId(), UserID="user-bill-1", PaRequestID=8, Message="PA-2026-00008 authorization has expired.",                NotificationType=NotificationType.ExpirationAlert,                IsRead=false, CreatedAt =now.AddDays(-17) },
        ]);
    }

    // ── Seed Audit Logs ───────────────────────────────────────────────────────
    private void SeedAuditLogs()
    {
        var now = DateTime.UtcNow;
        AuditLogs.AddRange([
            new AuditLog { ID=NextAuditId(), EntityName="PaRequest", EntityID ="1", Action="Created",    UserID="user-spec-1", UserName="Sarah Chen",     OccurredAt=now.AddDays(-20) },
            new AuditLog { ID=NextAuditId(), EntityName="PaRequest", EntityID ="1", Action="Submitted",  UserID="user-spec-1", UserName="Sarah Chen",     OccurredAt=now.AddDays(-18) },
            new AuditLog { ID=NextAuditId(), EntityName="PaRequest", EntityID ="1", Action="BeginReview",UserID="user-rev-1",  UserName="Robert Torres",  OccurredAt=now.AddDays(-16) },
            new AuditLog { ID=NextAuditId(), EntityName="PaRequest", EntityID ="1", Action="Approved",   UserID="user-rev-1",  UserName="Robert Torres",  OccurredAt=now.AddDays(-5)  },
            new AuditLog { ID=NextAuditId(), EntityName="PaRequest", EntityID ="2", Action="Created",    UserID="user-spec-1", UserName="Sarah Chen",     OccurredAt=now.AddDays(-16) },
            new AuditLog { ID=NextAuditId(), EntityName="PaRequest", EntityID ="2", Action="Denied",     UserID="user-rev-1",  UserName="Robert Torres",  OccurredAt=now.AddDays(-6)  },
            new AuditLog { ID=NextAuditId(), EntityName="PaRequest", EntityID ="8", Action="Expired",    UserID="SYSTEM",      UserName = "System",         OccurredAt=now.AddDays(-17) },
            new AuditLog { ID=NextAuditId(), EntityName="ApplicationUser", EntityID ="user-spec-1", Action="Login", UserID="user-spec-1", UserName="Sarah Chen", OccurredAt=now.AddHours(-2) },
        ]);
    }
}

/// <summary>Lightweight user record for the demo (replaces ApplicationUser / Identity).</summary>
public sealed class DemoUser
{
    public string Id          { get; init; } = string.Empty;
    public string FullName    { get; init; } = string.Empty;
    public string Email       { get; init; } = string.Empty;
    public string Role        { get; set; } = string.Empty;
    public string? Department { get; init; }
    public bool   IsActive    { get; set; } = true;
}
