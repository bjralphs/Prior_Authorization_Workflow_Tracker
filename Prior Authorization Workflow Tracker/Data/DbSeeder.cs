using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Data;

/// <summary>
/// Seeds reference data and synthetic demo accounts on first application start (§14).
///
/// Called only in Development environment from Program.cs.
/// Idempotent: all inserts are guarded with existence checks.
///
/// SYSTEM user: A reserved non-human account used by ExpirationJob for audit entries (§8.6).
/// It is created here and must exist before ExpirationJob registers its hosted service.
/// </summary>
public sealed class DbSeeder
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ILogger<DbSeeder> _logger;

    // Reserved SYSTEM account constants — never expose as a real login
    public const string SystemUserId = "00000000-0000-0000-0000-000000000001";
    public const string SystemUserName = "system@pademo.internal";

    public DbSeeder(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<DbSeeder> logger)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        // Migrations are applied by Program.cs before SeedAsync is called.
        // DbSeeder only seeds demo data and must not be called outside Development.

        await SeedRolesAsync();
        await SeedSystemUserAsync();
        await SeedDemoUsersAsync();
        await SeedInsurancePlansAsync();
        await SeedProcedureCodesAsync();
        await SeedDenialReasonsAsync();
        await SeedPaRequestsAsync();
        await SeedAuditLogsAsync();
        await SeedNotificationsAsync();

        _logger.LogInformation("Database seeding complete.");
    }

    // ── Roles ─────────────────────────────────────────────────────────────────

    private static readonly string[] Roles =
    [
        "Authorization Specialist",
        "Treating Provider",
        "Billing Manager",
        "Payer Reviewer",
        "Administrator"
    ];

    private async Task SeedRolesAsync()
    {
        foreach (var role in Roles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                await _roleManager.CreateAsync(new IdentityRole(role));
                _logger.LogInformation("Created role: {Role}", role);
            }
        }
    }

    // ── SYSTEM reserved user (§8.6) ───────────────────────────────────────────

    private async Task SeedSystemUserAsync()
    {
        if (await _userManager.FindByIdAsync(SystemUserId) is not null) return;

        var system = new ApplicationUser
        {
            Id = SystemUserId,
            UserName = SystemUserName,
            Email = SystemUserName,
            NormalizedUserName = SystemUserName.ToUpperInvariant(),
            NormalizedEmail = SystemUserName.ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = "SYSTEM",
            IsActive = false, // cannot login
            CreatedAt = DateTime.UtcNow
        };

        // System user has a random password — it is never used for login
        var result = await _userManager.CreateAsync(system, Guid.NewGuid().ToString("N") + "Aa1!");
        if (!result.Succeeded)
        {
            _logger.LogError("Failed to create SYSTEM user: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    // ── Demo accounts (§14.1) ─────────────────────────────────────────────────

    private record DemoUser(string Email, string Role, string FullName, string Department);

    private static readonly DemoUser[] DemoUsers =
    [
        new("specialist@pademo.com",  "Authorization Specialist", "Sarah Chen",     "Authorization"),
        new("specialist2@pademo.com", "Authorization Specialist", "David Park",     "Authorization"),
        new("provider@pademo.com",    "Treating Provider",        "Dr. Marcus Webb","Cardiology"),
        new("provider2@pademo.com",   "Treating Provider",        "Dr. Leila Mora", "Orthopedics"),
        new("provider3@pademo.com",   "Treating Provider",        "Dr. Sam Okafor", "Oncology"),
        new("billing@pademo.com",     "Billing Manager",          "Angela Torres",  "Billing"),
        new("reviewer@pademo.com",    "Payer Reviewer",           "James Hollis",   "Payer Review"),
        new("admin@pademo.com",       "Administrator",            "System Admin",   "IT"),
    ];

    private async Task SeedDemoUsersAsync()
    {
        foreach (var demo in DemoUsers)
        {
            if (await _userManager.FindByEmailAsync(demo.Email) is not null) continue;

            var user = new ApplicationUser
            {
                UserName = demo.Email,
                Email = demo.Email,
                EmailConfirmed = true,
                FullName = demo.FullName,
                Department = demo.Department,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, "Demo@1234");
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, demo.Role);
                _logger.LogInformation("Created demo user: {Email} ({Role})", demo.Email, demo.Role);
            }
            else
            {
                _logger.LogError("Failed to create user {Email}: {Errors}",
                    demo.Email, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }

    // ── Insurance Plans (§14.2 — 8 plans) ────────────────────────────────────

    private async Task SeedInsurancePlansAsync()
    {
        if (await _db.InsurancePlans.AnyAsync()) return;

        _db.InsurancePlans.AddRange(
            new InsurancePlan { PlanName = "BlueShield HMO Gold",    PayerName = "BlueShield",   PlanType = "HMO",      RoutineDecisionDays = 14, UrgentDecisionDays = 3, EmergentDecisionDays = 1, PhoneNumber = "800-555-0101", IsActive = true },
            new InsurancePlan { PlanName = "BlueShield PPO Silver",  PayerName = "BlueShield",   PlanType = "PPO",      RoutineDecisionDays = 15, UrgentDecisionDays = 3, EmergentDecisionDays = 1, PhoneNumber = "800-555-0102", IsActive = true },
            new InsurancePlan { PlanName = "Aetna PPO Plus",         PayerName = "Aetna",        PlanType = "PPO",      RoutineDecisionDays = 10, UrgentDecisionDays = 2, EmergentDecisionDays = 1, PhoneNumber = "800-555-0201", IsActive = true },
            new InsurancePlan { PlanName = "Aetna HMO Basic",        PayerName = "Aetna",        PlanType = "HMO",      RoutineDecisionDays = 14, UrgentDecisionDays = 3, EmergentDecisionDays = 1, PhoneNumber = "800-555-0202", IsActive = true },
            new InsurancePlan { PlanName = "UnitedHealth EPO Select", PayerName = "UnitedHealth", PlanType = "EPO",      RoutineDecisionDays = 7,  UrgentDecisionDays = 2, EmergentDecisionDays = 1, PhoneNumber = "800-555-0301", IsActive = true },
            new InsurancePlan { PlanName = "Medicare Part B",         PayerName = "CMS",          PlanType = "Medicare", RoutineDecisionDays = 14, UrgentDecisionDays = 3, EmergentDecisionDays = 1, PhoneNumber = "800-555-1800", IsActive = true },
            new InsurancePlan { PlanName = "Medicaid Standard",       PayerName = "State Agency", PlanType = "Medicaid", RoutineDecisionDays = 21, UrgentDecisionDays = 5, EmergentDecisionDays = 1, PhoneNumber = "800-555-1900", IsActive = true },
            new InsurancePlan { PlanName = "Cigna PPO National",      PayerName = "Cigna",        PlanType = "PPO",      RoutineDecisionDays = 10, UrgentDecisionDays = 2, EmergentDecisionDays = 1, PhoneNumber = "800-555-0401", IsActive = true }
        );

        await _db.SaveChangesAsync();
        _logger.LogInformation("Seeded {Count} insurance plans.", 8);
    }

    // ── Procedure Codes (§14.2 — 25 CPT codes) ───────────────────────────────

    private async Task SeedProcedureCodesAsync()
    {
        if (await _db.ProcedureCodes.AnyAsync()) return;

        _db.ProcedureCodes.AddRange(
            new ProcedureCode { Code = "27447", Description = "Total knee arthroplasty",                RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 90  },
            new ProcedureCode { Code = "27130", Description = "Total hip arthroplasty",                 RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 90  },
            new ProcedureCode { Code = "23472", Description = "Total shoulder arthroplasty",            RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 90  },
            new ProcedureCode { Code = "33533", Description = "CABG, arterial, single",                 RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 30  },
            new ProcedureCode { Code = "43239", Description = "Upper GI endoscopy with biopsy",        RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 30  },
            new ProcedureCode { Code = "70553", Description = "MRI brain with contrast",               RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 60  },
            new ProcedureCode { Code = "71250", Description = "CT thorax without contrast",            RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 60  },
            new ProcedureCode { Code = "72148", Description = "MRI lumbar spine without contrast",     RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 60  },
            new ProcedureCode { Code = "93306", Description = "Echocardiography with Doppler",         RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 365 },
            new ProcedureCode { Code = "64483", Description = "Injection, epidural steroid, lumbar",   RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 180 },
            new ProcedureCode { Code = "90837", Description = "Psychotherapy, 60 minutes",             RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 365 },
            new ProcedureCode { Code = "96413", Description = "Chemotherapy administration, IV",       RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 90  },
            new ProcedureCode { Code = "97110", Description = "Therapeutic exercises",                 RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 60  },
            new ProcedureCode { Code = "97530", Description = "Therapeutic activities",                RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 60  },
            new ProcedureCode { Code = "99213", Description = "Office visit, established patient, low", RequiresPriorAuth = false, TypicalAuthorizationDurationDays = 0   },
            new ProcedureCode { Code = "76817", Description = "Ultrasound, pregnant uterus",           RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 180 },
            new ProcedureCode { Code = "45378", Description = "Colonoscopy, diagnostic",               RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 30  },
            new ProcedureCode { Code = "19301", Description = "Partial mastectomy",                    RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 30  },
            new ProcedureCode { Code = "47562", Description = "Laparoscopic cholecystectomy",          RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 30  },
            new ProcedureCode { Code = "43280", Description = "Laparoscopic fundoplication",           RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 30  },
            new ProcedureCode { Code = "28296", Description = "Correction of hallux valgus",           RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 30  },
            new ProcedureCode { Code = "26116", Description = "Excision of ganglion, wrist",           RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 30  },
            new ProcedureCode { Code = "66984", Description = "Cataract removal with IOL",             RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 30  },
            new ProcedureCode { Code = "61510", Description = "Craniotomy for excision of brain tumor", RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 30  },
            new ProcedureCode { Code = "50590", Description = "Lithotripsy, extracorporeal shock wave", RequiresPriorAuth = true,  TypicalAuthorizationDurationDays = 30  }
        );

        await _db.SaveChangesAsync();
        _logger.LogInformation("Seeded {Count} procedure codes.", 25);
    }

    // ── Denial Reasons (§14.2 — 12 codes) ────────────────────────────────────

    private async Task SeedDenialReasonsAsync()
    {
        if (await _db.DenialReasons.AnyAsync()) return;

        _db.DenialReasons.AddRange(
            new DenialReason { Code = "MED-NOT-NECESSARY", Description = "Not medically necessary",                      IsAppealable = true,  AppealDeadlineDays = 30 },
            new DenialReason { Code = "NOT-COVERED",       Description = "Service not covered under plan",               IsAppealable = true,  AppealDeadlineDays = 30 },
            new DenialReason { Code = "INCOMPLETE-INFO",   Description = "Insufficient clinical documentation",          IsAppealable = true,  AppealDeadlineDays = 60 },
            new DenialReason { Code = "PRIOR-TX-REQUIRED", Description = "Conservative treatment not yet attempted",     IsAppealable = true,  AppealDeadlineDays = 30 },
            new DenialReason { Code = "OUT-OF-NETWORK",    Description = "Provider is out of network",                   IsAppealable = true,  AppealDeadlineDays = 30 },
            new DenialReason { Code = "EXPERIMENTAL",      Description = "Treatment deemed experimental/investigational", IsAppealable = true,  AppealDeadlineDays = 60 },
            new DenialReason { Code = "DUPLICATE",         Description = "Duplicate request",                            IsAppealable = false, AppealDeadlineDays = 0  },
            new DenialReason { Code = "ADMIN-ERROR",       Description = "Administrative / billing error",               IsAppealable = false, AppealDeadlineDays = 0  },
            new DenialReason { Code = "NO-COVERAGE",       Description = "No active coverage on date of service",        IsAppealable = true,  AppealDeadlineDays = 30 },
            new DenialReason { Code = "FREQ-LIMIT",        Description = "Frequency limit exceeded",                     IsAppealable = true,  AppealDeadlineDays = 30 },
            new DenialReason { Code = "WRONG-LEVEL",       Description = "Level of service not appropriate",             IsAppealable = true,  AppealDeadlineDays = 30 },
            new DenialReason { Code = "NON-FORMULARY",     Description = "Drug not on formulary",                        IsAppealable = true,  AppealDeadlineDays = 14 }
        );

        await _db.SaveChangesAsync();
        _logger.LogInformation("Seeded {Count} denial reasons.", 12);
    }

    // ── PA Requests (§14.3 — 50 synthetic requests across all statuses) ───────

    private async Task SeedPaRequestsAsync()
    {
        if (await _db.PaRequests.IgnoreQueryFilters().AnyAsync()) return;

        var specialist1 = await _userManager.FindByEmailAsync("specialist@pademo.com");
        var specialist2 = await _userManager.FindByEmailAsync("specialist2@pademo.com");
        var provider1   = await _userManager.FindByEmailAsync("provider@pademo.com");
        var provider2   = await _userManager.FindByEmailAsync("provider2@pademo.com");
        var provider3   = await _userManager.FindByEmailAsync("provider3@pademo.com");
        var reviewer    = await _userManager.FindByEmailAsync("reviewer@pademo.com");

        if (specialist1 is null || specialist2 is null || provider1 is null ||
            provider2 is null || provider3 is null || reviewer is null)
        {
            _logger.LogWarning("Skipping PA request seeding — demo users not yet created.");
            return;
        }

        var plans = await _db.InsurancePlans.ToListAsync();
        var codes = await _db.ProcedureCodes.Where(c => c.RequiresPriorAuth).ToListAsync();
        var denials = await _db.DenialReasons.ToListAsync();

        var baseDate = DateTime.UtcNow.Date;  // all dates relative to today so demo data stays fresh
        var requests = new List<PaRequest>();
        var histories = new List<PaStatusHistory>();
        var comments = new List<PaComment>();
        int seq = 1;

        PaRequest MakeRequest(
            string submittedById, string providerId,
            string mrn, string patientName, DateTime dob,
            int planIdx, int codeIdx,
            string icd10, string justification,
            PaStatus status, PaPriority priority,
            int daysAgo, bool isDeleted = false) =>
            new()
            {
                RequestNumber = $"PA-2026-{seq++:D5}",
                PatientMrn = mrn,
                PatientName = patientName,
                PatientDob = dob,
                InsurancePlanID = plans[planIdx].ID,
                ProviderID = providerId,
                SubmittedByUserID = submittedById,
                ProcedureCodeID = codes[codeIdx].ID,
                DiagnosisCode = icd10,
                ClinicalJustification = justification.PadRight(55),
                Status = status,
                Priority = priority,
                SubmittedAt = status == PaStatus.Draft ? null : baseDate.AddDays(-daysAgo),
                DecisionDueDate = status == PaStatus.Draft ? null :
                    baseDate.AddDays(-daysAgo + priority switch
                    {
                        PaPriority.Urgent => plans[planIdx].UrgentDecisionDays,
                        PaPriority.Emergent => plans[planIdx].EmergentDecisionDays,
                        _ => plans[planIdx].RoutineDecisionDays
                    }),
                ApprovedUnitsRequested = 1,
                CreatedAt = baseDate.AddDays(-daysAgo - 1),
                UpdatedAt = baseDate.AddDays(-daysAgo),
                IsDeleted = isDeleted
            };

        // Status distribution (§14.3)
        // Draft: 3
        requests.Add(MakeRequest(specialist1.Id, provider1.Id, "MRN-1001", "Alice Johnson",    new DateTime(1975, 3, 14), 0, 0, "M54.5",  "Patient has failed conservative therapy for lumbar pain.", PaStatus.Draft, PaPriority.Routine, 0));
        requests.Add(MakeRequest(specialist2.Id, provider2.Id, "MRN-1002", "Brian Kim",        new DateTime(1988, 7, 22), 1, 1, "M16.11", "Bilateral hip OA severe, conservative treatment failed.", PaStatus.Draft, PaPriority.Routine, 0));
        requests.Add(MakeRequest(specialist1.Id, provider3.Id, "MRN-1003", "Carol Davis",      new DateTime(1962, 11, 5), 2, 11, "C34.10", "Stage IIIA NSCLC, chemo per oncology plan.",             PaStatus.Draft, PaPriority.Urgent,   0));

        // Submitted: 6
        requests.Add(MakeRequest(specialist1.Id, provider1.Id, "MRN-1004", "Diana Lee",        new DateTime(1971, 4, 18), 3, 5, "G35",    "MS patient, new gadolinium-enhancing lesion on MRI.",    PaStatus.Submitted, PaPriority.Urgent,   5));
        requests.Add(MakeRequest(specialist2.Id, provider2.Id, "MRN-1005", "Edward Brown",     new DateTime(1955, 8, 30), 4, 7, "M51.16", "Lumbar radiculopathy, failed PT x 6 weeks.",             PaStatus.Submitted, PaPriority.Routine,  8));
        requests.Add(MakeRequest(specialist1.Id, provider3.Id, "MRN-1006", "Fiona Martinez",   new DateTime(1940, 2, 12), 5, 9, "M54.4",  "Lumbosacral radiculopathy, severe pain score 8/10.",     PaStatus.Submitted, PaPriority.Urgent,   3));
        requests.Add(MakeRequest(specialist2.Id, provider1.Id, "MRN-1007", "George Wilson",    new DateTime(1983, 12, 1), 6, 12, "F41.1",  "Generalized anxiety disorder, failed SSRI trial.",       PaStatus.Submitted, PaPriority.Routine,  10));
        requests.Add(MakeRequest(specialist1.Id, provider2.Id, "MRN-1008", "Hannah Taylor",    new DateTime(1968, 6, 28), 7, 8, "I25.10",  "Coronary artery disease, EF 35%, echo needed.",          PaStatus.Submitted, PaPriority.Urgent,   2));
        requests.Add(MakeRequest(specialist2.Id, provider3.Id, "MRN-1009", "Ivan Thomas",      new DateTime(1945, 9, 15), 0, 4, "K21.0",  "GERD refractory to PPI, EGD indicated.",                PaStatus.Submitted, PaPriority.Routine,  12));

        // PendingInfo: 5
        requests.Add(MakeRequest(specialist1.Id, provider1.Id, "MRN-1010", "Julia Anderson",   new DateTime(1979, 5, 7),  1, 2, "M75.1",  "Rotator cuff tear, full-thickness per MRI.",             PaStatus.PendingInfo, PaPriority.Routine, 20));
        requests.Add(MakeRequest(specialist2.Id, provider2.Id, "MRN-1011", "Kevin Jackson",    new DateTime(1991, 1, 22), 2, 0, "M17.11", "Unicompartmental knee OA, failed injections.",           PaStatus.PendingInfo, PaPriority.Routine, 15));
        requests.Add(MakeRequest(specialist1.Id, provider3.Id, "MRN-1012", "Laura White",      new DateTime(1958, 3, 3),  3, 16, "K80.20", "Cholelithiasis with acute cholecystitis.",               PaStatus.PendingInfo, PaPriority.Urgent,   7));
        requests.Add(MakeRequest(specialist2.Id, provider1.Id, "MRN-1013", "Marcus Harris",    new DateTime(1947, 10, 18),4, 6, "J18.9",  "Community-acquired pneumonia, CT chest needed.",         PaStatus.PendingInfo, PaPriority.Urgent,   6));
        requests.Add(MakeRequest(specialist1.Id, provider2.Id, "MRN-1014", "Nancy Garcia",     new DateTime(1974, 8, 14), 5, 3, "I25.110","Triple vessel disease, CABG evaluation.",                PaStatus.PendingInfo, PaPriority.Emergent,  4));

        // UnderReview: 7
        requests.Add(MakeRequest(specialist1.Id, provider3.Id, "MRN-1015", "Oliver Martinez",  new DateTime(1985, 2, 28), 6, 5, "C71.9",  "Brain tumor resection, MRI for surgical planning.",      PaStatus.UnderReview, PaPriority.Urgent,   14));
        requests.Add(MakeRequest(specialist2.Id, provider1.Id, "MRN-1016", "Patricia Robinson", new DateTime(1962, 7, 4), 7, 22, "H26.9",  "Visually significant cataract, BCVA 20/200.",            PaStatus.UnderReview, PaPriority.Routine,  21));
        requests.Add(MakeRequest(specialist1.Id, provider2.Id, "MRN-1017", "Quinn Clark",      new DateTime(1970, 11, 11),0, 1, "M16.12", "Hip OA with avascular necrosis.",                        PaStatus.UnderReview, PaPriority.Routine,  18));
        requests.Add(MakeRequest(specialist2.Id, provider3.Id, "MRN-1018", "Rachel Lewis",     new DateTime(1953, 4, 25), 1, 19, "K44.9",  "Hiatal hernia with severe GERD, surgery indicated.",    PaStatus.UnderReview, PaPriority.Routine,  22));
        requests.Add(MakeRequest(specialist1.Id, provider1.Id, "MRN-1019", "Steven Lee",       new DateTime(1937, 9, 6),  2, 21, "M20.10", "Bunionectomy, conservative treatment unsuccessful.",     PaStatus.UnderReview, PaPriority.Routine,  16));
        requests.Add(MakeRequest(specialist2.Id, provider2.Id, "MRN-1020", "Tina Young",       new DateTime(1980, 12, 20),3, 23, "N20.0",  "Nephrolithiasis 1.2 cm, ESWL indicated.",               PaStatus.UnderReview, PaPriority.Urgent,   9));
        requests.Add(MakeRequest(specialist1.Id, provider3.Id, "MRN-1021", "Uma Hernandez",    new DateTime(1965, 6, 17), 4, 15, "O30.003","Twin pregnancy, anatomy ultrasound required.",           PaStatus.UnderReview, PaPriority.Urgent,   11));

        // Approved: 15
        var approvedStart = DateTime.UtcNow.Date; // auth start dates relative to today
        (string mrn, string name, DateTime dob, int plan, int code, string icd10, string just, PaPriority pri, int ago, DateTime authStart, int authDays)[] approvedData =
        [
            ("MRN-2001","Alexander King",   new(1972,3,8), 0, 13, "M62.9",  "Physical therapy post-op hip",  PaPriority.Routine,  45, approvedStart.AddDays(-44), 60),
            ("MRN-2002","Bella Scott",      new(1989,7,3), 1, 12, "F32.1",  "Moderate depression, therapy",  PaPriority.Routine,  50, approvedStart.AddDays(-49), 365),
            ("MRN-2003","Carlos Adams",     new(1955,11,14),2,0,  "M17.12","TKA right knee",                PaPriority.Routine,  60, approvedStart.AddDays(-59), 90),
            ("MRN-2004","Diana Baker",      new(1948,4,22), 3, 1, "M16.32","Total hip replacement left",     PaPriority.Routine,  55, approvedStart.AddDays(-54), 90),
            ("MRN-2005","Evan Cooper",      new(1966,8,19), 4, 11,"C50.912","Breast cancer chemo",           PaPriority.Urgent,   35, approvedStart.AddDays(-34), 90),
            ("MRN-2006","Fiona Price",      new(1977,1,30), 5, 5, "G43.909","Migraine, MRI brain",           PaPriority.Routine,  40, approvedStart.AddDays(-39), 60),
            ("MRN-2007","George Reed",      new(1940,6,11), 6, 8, "I50.9",  "Heart failure, echo",           PaPriority.Urgent,   30, approvedStart.AddDays(-29), 365),
            ("MRN-2008","Helen Morgan",     new(1983,9,5),  7, 7, "M51.16","Lumbar disc herniation MRI",     PaPriority.Routine,  42, approvedStart.AddDays(-41), 60),
            ("MRN-2009","Ivan Bell",        new(1959,2,27), 0, 4, "K57.30","Diverticulosis, colonoscopy",    PaPriority.Routine,  38, approvedStart.AddDays(-37), 30),
            ("MRN-2010","Julia Murphy",     new(1971,5,16), 1, 3, "I25.10","Three vessel disease, CABG",     PaPriority.Urgent,   28, approvedStart.AddDays(-27), 30),
            ("MRN-2011","Kevin Bailey",     new(1984,10,4), 2,14, "K35.80","Appendicitis, laparoscopic",    PaPriority.Emergent,  14, approvedStart.AddDays(-13), 30),
            ("MRN-2012","Laura Rivera",     new(1947,12,21),3, 22,"H26.9",  "Dense nuclear cataract",        PaPriority.Routine,  65, approvedStart.AddDays(-64), 30),
            ("MRN-2013","Marcus Cox",       new(1968,3,18), 4, 9, "M54.41","Epidural steroid, lumbar",       PaPriority.Routine,  48, approvedStart.AddDays(-47), 180),
            ("MRN-2014","Nina Richardson",  new(1993,7,12), 5,18, "K80.00","Gallstones with cholecystitis", PaPriority.Urgent,   22, approvedStart.AddDays(-21), 30),
            ("MRN-2015","Oscar Ward",       new(1950,4,3),  6, 6, "J45.51","Severe asthma, CT chest",        PaPriority.Routine,  53, approvedStart.AddDays(-52), 60),
        ];

        var approvedIdx = 0;
        foreach (var (mrn, name, dob, plan, code, icd10, just, pri, ago, authStart, authDays) in approvedData)
        {
            // Spread across both specialists and all three providers (§14.2 variety requirement)
            var specId = approvedIdx % 2 == 0 ? specialist1.Id : specialist2.Id;
            var provId = approvedIdx < 5 ? provider1.Id : approvedIdx < 10 ? provider2.Id : provider3.Id;
            var req = MakeRequest(specId, provId, mrn, name, dob, plan, code, icd10, just, PaStatus.Approved, pri, ago);
            req.DecisionRenderedAt = baseDate.AddDays(-ago + plans[plan].RoutineDecisionDays - 2);
            req.ApprovedUnitsGranted = 1;
            req.AuthorizationNumber = $"AUTH-{authStart:yyyyMMdd}-{seq:X6}";
            req.AuthorizationStartDate = authStart.Date;
            req.AuthorizationEndDate = authStart.Date.AddDays(authDays);
            req.ReviewerUserID = reviewer.Id;
            requests.Add(req);
            approvedIdx++;
        }

        // Denied: 7
        (string mrn, string name, DateTime dob, int plan, int code, string icd10, string just, PaPriority pri, int ago, int denialIdx)[] deniedData =
        [
            ("MRN-3001","Peter Simpson",  new(1982,8,14), 0, 0, "M17.11","Conservative treatment not tried",   PaPriority.Routine, 30, 3),
            ("MRN-3002","Quinn Foster",   new(1969,2,25), 1, 5, "G89.29","Non-specific pain, MRI not indicated",PaPriority.Routine, 25, 0),
            ("MRN-3003","Rachel Hughes",  new(1945,11,3), 2,11, "C34.12","Experimental regimen not approved",   PaPriority.Urgent,  20, 5),
            ("MRN-3004","Samuel Jenkins", new(1977,6,7),  3, 7, "M51.36","Conservative care ongoing",           PaPriority.Routine, 35, 3),
            ("MRN-3005","Tara Powell",    new(1991,4,12), 4,16, "O30.009","Level of service not appropriate",   PaPriority.Routine, 28, 9),
            ("MRN-3006","Uma Griffin",    new(1959,9,22), 5, 8, "I25.110","Out of network provider",            PaPriority.Urgent,  18, 4),
            ("MRN-3007","Victor Watson",  new(1973,1,17), 6, 4, "K57.31","Service not covered under plan",      PaPriority.Routine, 22, 1),
        ];

        foreach (var (mrn, name, dob, plan, code, icd10, just, pri, ago, denialIdx) in deniedData)
        {
            var req = MakeRequest(specialist1.Id, provider2.Id, mrn, name, dob, plan, code, icd10, just, PaStatus.Denied, pri, ago);
            req.DenialReasonID = denials[denialIdx].ID;
            req.DenialNotes = $"Denial per clinical guidelines. {denials[denialIdx].Description}.";
            req.DecisionRenderedAt = baseDate.AddDays(-ago + plans[plan].RoutineDecisionDays - 1);
            req.ReviewerUserID = reviewer.Id;
            requests.Add(req);
        }

        // Appealed: 3
        (string mrn, string name, DateTime dob, int plan, int code, string icd10, string just, int ago, int denialIdx)[] appealedData =
        [
            ("MRN-4001","Wendy Brooks",  new(1981,5,9),  0, 0, "M17.12","Appeal: new clinical evidence submitted.",  25, 0),
            ("MRN-4002","Xavier Hayes",  new(1953,12,4), 1, 3, "I25.110","Appeal: documentation resubmitted.",       20, 3),
            ("MRN-4003","Yasmin Price",  new(1976,3,20), 2,11, "C50.912","Appeal: oncologist letter attached.",       15, 5),
        ];

        foreach (var (mrn, name, dob, plan, code, icd10, just, ago, denialIdx) in appealedData)
        {
            var req = MakeRequest(specialist1.Id, provider3.Id, mrn, name, dob, plan, code, icd10, just, PaStatus.Appealed, PaPriority.Urgent, ago);
            req.DenialReasonID = denials[denialIdx].ID;
            req.DecisionRenderedAt = baseDate.AddDays(-ago - 5);
            req.ReviewerUserID = reviewer.Id;
            requests.Add(req);
        }

        // Withdrawn: 2
        requests.Add(MakeRequest(specialist2.Id, provider1.Id, "MRN-5001", "Zachary Evans",  new DateTime(1980,8,25), 3, 12, "F41.0",  "Withdrawn by patient request.",        PaStatus.Withdrawn, PaPriority.Routine, 40));
        requests.Add(MakeRequest(specialist2.Id, provider2.Id, "MRN-5002", "Amber Collins",  new DateTime(1967,6,14), 4, 9,  "M54.41", "Withdrawn — procedure no longer needed.", PaStatus.Withdrawn, PaPriority.Routine, 35));

        // Expired: 2 (AuthorizationEndDate in the past)
        var expired1 = MakeRequest(specialist1.Id, provider1.Id, "MRN-6001", "Brandon Mitchell", new DateTime(1959, 2, 12), 5, 0, "M17.11", "TKA approved, authorization expired.", PaStatus.Expired, PaPriority.Routine, 180);
        expired1.DecisionRenderedAt    = baseDate.AddDays(-175);
        expired1.ApprovedUnitsGranted  = 1;
        expired1.AuthorizationStartDate = baseDate.AddDays(-174);
        expired1.AuthorizationEndDate   = baseDate.AddDays(-84);   // 90-day auth, ended ~3 months ago
        expired1.AuthorizationNumber    = $"AUTH-{expired1.AuthorizationStartDate.Value:yyyyMMdd}-{seq:X6}";
        expired1.ReviewerUserID = reviewer.Id;
        requests.Add(expired1);

        var expired2 = MakeRequest(specialist2.Id, provider2.Id, "MRN-6002", "Christine Torres", new DateTime(1972, 9, 3), 6, 1, "M16.12", "Hip replacement approved, auth lapsed.", PaStatus.Expired, PaPriority.Routine, 200);
        expired2.DecisionRenderedAt    = baseDate.AddDays(-195);
        expired2.ApprovedUnitsGranted  = 1;
        expired2.AuthorizationStartDate = baseDate.AddDays(-194);
        expired2.AuthorizationEndDate   = baseDate.AddDays(-104);  // 90-day auth, ended ~3.5 months ago
        expired2.AuthorizationNumber    = $"AUTH-{expired2.AuthorizationStartDate.Value:yyyyMMdd}-{seq:X6}";
        expired2.ReviewerUserID = reviewer.Id;
        requests.Add(expired2);

        _db.PaRequests.AddRange(requests);
        await _db.SaveChangesAsync();

        // ── Status history (~3 rows per request) ─────────────────────────────

        foreach (var req in requests.Where(r => r.Status != PaStatus.Draft))
        {
            histories.Add(new PaStatusHistory
            {
                PaRequestID = req.ID,
                FromStatus = PaStatus.Draft,
                ToStatus = PaStatus.Submitted,
                ChangedByUserID = req.SubmittedByUserID,
                ChangedAt = req.SubmittedAt!.Value
            });

            if (req.Status is PaStatus.UnderReview or PaStatus.Approved or PaStatus.Denied
                              or PaStatus.Appealed or PaStatus.Expired)
            {
                histories.Add(new PaStatusHistory
                {
                    PaRequestID = req.ID,
                    FromStatus = PaStatus.Submitted,
                    ToStatus = PaStatus.UnderReview,
                    ChangedByUserID = reviewer.Id,
                    ChangedAt = req.SubmittedAt!.Value.AddDays(1)
                });
            }

            // Expired requests went through Approved before expiring — include that step (§8.4)
            if (req.Status == PaStatus.Approved || req.Status == PaStatus.Expired)
            {
                histories.Add(new PaStatusHistory
                {
                    PaRequestID = req.ID,
                    FromStatus = PaStatus.UnderReview,
                    ToStatus = PaStatus.Approved,
                    ChangedByUserID = reviewer.Id,
                    ChangedAt = req.DecisionRenderedAt!.Value
                });
            }

            if (req.Status is PaStatus.Denied or PaStatus.Appealed)
            {
                histories.Add(new PaStatusHistory
                {
                    PaRequestID = req.ID,
                    FromStatus = PaStatus.UnderReview,
                    ToStatus = PaStatus.Denied,
                    ChangedByUserID = reviewer.Id,
                    ChangedAt = req.DecisionRenderedAt!.Value
                });
            }

            if (req.Status == PaStatus.Appealed)
            {
                histories.Add(new PaStatusHistory
                {
                    PaRequestID = req.ID,
                    FromStatus = PaStatus.Denied,
                    ToStatus = PaStatus.Appealed,
                    ChangedByUserID = req.SubmittedByUserID,
                    ChangedAt = req.DecisionRenderedAt!.Value.AddDays(5),
                    ChangeReason = "Appeal filed within deadline."
                });
            }

            if (req.Status == PaStatus.PendingInfo)
            {
                histories.Add(new PaStatusHistory
                {
                    PaRequestID = req.ID,
                    FromStatus = PaStatus.Submitted,
                    ToStatus = PaStatus.PendingInfo,
                    ChangedByUserID = reviewer.Id,
                    ChangedAt = req.SubmittedAt!.Value.AddDays(2),
                    ChangeReason = "Additional clinical documentation required."
                });
            }

            if (req.Status == PaStatus.Withdrawn)
            {
                histories.Add(new PaStatusHistory
                {
                    PaRequestID = req.ID,
                    FromStatus = PaStatus.Submitted,
                    ToStatus = PaStatus.Withdrawn,
                    ChangedByUserID = req.SubmittedByUserID,
                    ChangedAt = req.SubmittedAt!.Value.AddDays(3),
                    ChangeReason = "Request withdrawn by specialist."
                });
            }

            if (req.Status == PaStatus.Expired)
            {
                histories.Add(new PaStatusHistory
                {
                    PaRequestID = req.ID,
                    FromStatus = PaStatus.Approved,
                    ToStatus = PaStatus.Expired,
                    ChangedByUserID = SystemUserId,
                    ChangedAt = req.AuthorizationEndDate!.Value.AddDays(1),
                    ChangeReason = "Authorization expired — automated nightly job."
                });
            }
        }

        _db.PaStatusHistories.AddRange(histories);

        // ── Sample comments (~72 across all status groups, approaching §14.2 ~80 target) ────

        var allSubmitted   = requests.Where(r => r.Status == PaStatus.Submitted).ToList();
        var allUnderReview = requests.Where(r => r.Status == PaStatus.UnderReview).ToList();
        var allPending     = requests.Where(r => r.Status == PaStatus.PendingInfo).ToList();
        var allApproved    = requests.Where(r => r.Status == PaStatus.Approved).Take(10).ToList();
        var allDenied      = requests.Where(r => r.Status == PaStatus.Denied).ToList();
        var allAppealed    = requests.Where(r => r.Status == PaStatus.Appealed).ToList();
        var allWithdrawn   = requests.Where(r => r.Status == PaStatus.Withdrawn).ToList();

        // Submitted: specialist submits with notes (6 × 1 = 6)
        foreach (var req in allSubmitted)
            comments.Add(new PaComment { PaRequestID = req.ID, CommentText = "Clinical documentation attached. Please review at earliest convenience.", IsInternalOnly = false, AuthoredByUserID = req.SubmittedByUserID, AuthoredAt = req.SubmittedAt!.Value.AddHours(2) });

        // UnderReview: reviewer internal notes (7 × 2 = 14)
        foreach (var req in allUnderReview)
        {
            comments.Add(new PaComment { PaRequestID = req.ID, CommentText = "Request under clinical review. Decision expected within SLA window.", IsInternalOnly = true, AuthoredByUserID = reviewer.Id, AuthoredAt = req.SubmittedAt!.Value.AddDays(2) });
            comments.Add(new PaComment { PaRequestID = req.ID, CommentText = "Imaging reports reviewed. Proceeding with clinical criteria evaluation.", IsInternalOnly = true, AuthoredByUserID = reviewer.Id, AuthoredAt = req.SubmittedAt!.Value.AddDays(3) });
        }

        // PendingInfo: reviewer requests info, specialist responds (5 × 2 = 10)
        foreach (var req in allPending)
        {
            comments.Add(new PaComment { PaRequestID = req.ID, CommentText = "Please provide prior treatment records and recent lab work.", IsInternalOnly = false, AuthoredByUserID = reviewer.Id, AuthoredAt = req.SubmittedAt!.Value.AddDays(2) });
            comments.Add(new PaComment { PaRequestID = req.ID, CommentText = "Additional documentation uploaded. Labs and imaging report from last 6 months attached.", IsInternalOnly = false, AuthoredByUserID = req.SubmittedByUserID, AuthoredAt = req.SubmittedAt!.Value.AddDays(4) });
        }

        // Approved: approval note + specialist follow-up (10 × 2 = 20)
        foreach (var req in allApproved)
        {
            comments.Add(new PaComment { PaRequestID = req.ID, CommentText = "Authorization approved. Patient may proceed with scheduled procedure.", IsInternalOnly = false, AuthoredByUserID = reviewer.Id, AuthoredAt = req.DecisionRenderedAt!.Value });
            comments.Add(new PaComment { PaRequestID = req.ID, CommentText = "Authorization number communicated to provider office.", IsInternalOnly = true, AuthoredByUserID = req.SubmittedByUserID, AuthoredAt = req.DecisionRenderedAt!.Value.AddHours(2) });
        }

        // Denied: denial note + specialist review (7 × 2 = 14)
        foreach (var req in allDenied)
        {
            comments.Add(new PaComment { PaRequestID = req.ID, CommentText = "Request denied per clinical criteria. Please review denial letter for appeal options.", IsInternalOnly = false, AuthoredByUserID = reviewer.Id, AuthoredAt = req.DecisionRenderedAt!.Value });
            comments.Add(new PaComment { PaRequestID = req.ID, CommentText = "Denial received. Reviewing appeal eligibility with provider.", IsInternalOnly = true, AuthoredByUserID = req.SubmittedByUserID, AuthoredAt = req.DecisionRenderedAt!.Value.AddDays(1) });
        }

        // Appealed: appeal filed + secondary review note (3 × 2 = 6)
        foreach (var req in allAppealed)
        {
            comments.Add(new PaComment { PaRequestID = req.ID, CommentText = "Appeal filed. Additional clinical evidence submitted with appeal packet.", IsInternalOnly = false, AuthoredByUserID = req.SubmittedByUserID, AuthoredAt = req.DecisionRenderedAt!.Value.AddDays(5) });
            comments.Add(new PaComment { PaRequestID = req.ID, CommentText = "Appeal packet under secondary clinical review.", IsInternalOnly = true, AuthoredByUserID = reviewer.Id, AuthoredAt = req.DecisionRenderedAt!.Value.AddDays(7) });
        }

        // Withdrawn: closure note (2 × 1 = 2)
        foreach (var req in allWithdrawn)
            comments.Add(new PaComment { PaRequestID = req.ID, CommentText = "Request withdrawn at patient/provider request. No further action required.", IsInternalOnly = false, AuthoredByUserID = req.SubmittedByUserID, AuthoredAt = req.SubmittedAt!.Value.AddDays(3) });

        _db.PaComments.AddRange(comments);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Seeded {Requests} PA requests, {Histories} status history rows, {Comments} comments.",
            requests.Count, histories.Count, comments.Count);
    }

    // ── Audit Log Rows (§14.2 — ~300 rows for demo viability) ──────────────────────

    private async Task SeedAuditLogsAsync()
    {
        if (await _db.AuditLogs.AnyAsync()) return;

        var specialist1 = await _userManager.FindByEmailAsync("specialist@pademo.com");
        var specialist2 = await _userManager.FindByEmailAsync("specialist2@pademo.com");
        var reviewer    = await _userManager.FindByEmailAsync("reviewer@pademo.com");
        var admin       = await _userManager.FindByEmailAsync("admin@pademo.com");
        var billing     = await _userManager.FindByEmailAsync("billing@pademo.com");

        if (specialist1 is null || specialist2 is null || reviewer is null)
        {
            _logger.LogWarning("Skipping AuditLog seeding — demo users not yet created.");
            return;
        }

        var requests = await _db.PaRequests.IgnoreQueryFilters().ToListAsync();
        if (requests.Count == 0)
        {
            _logger.LogWarning("Skipping AuditLog seeding — PA requests not yet seeded.");
            return;
        }

        var auditLogs = new List<AuditLog>();
        var baseDate  = DateTime.UtcNow.Date; // consistent with SeedPaRequestsAsync

        // --- Helper: generate a correlation ID in the same format as CorrelationIdMiddleware ---
        var rng = 0;
        string NextCid() => (++rng).ToString("x16").PadLeft(16, '0')[..16];

        AuditLog Log(string userId, string userName, string entity, string entityId,
            string action, DateTime at, string? oldValues = null, string? newValues = null) => new()
        {
            UserID        = userId,
            UserName      = userName,
            EntityName    = entity,
            EntityID      = entityId,
            Action        = action,
            OldValues     = oldValues,
            NewValues     = newValues,
            IpAddress     = "127.0.0.1",
            CorrelationId = NextCid(),
            OccurredAt    = at,
        };

        // 1. Audit rows for each PA request creation (Specialist submits)
        foreach (var req in requests.Where(r => r.Status != PaStatus.Draft))
        {
            var submitAt = req.SubmittedAt ?? baseDate;
            var spec     = req.SubmittedByUserID == specialist1.Id ? specialist1 : specialist2;
            auditLogs.Add(Log(spec.Id, spec.FullName, "PaRequest", req.ID.ToString(),
                "Create", submitAt.AddMinutes(-30),
                null, $"{{\"RequestNumber\":\"{req.RequestNumber}\",\"Status\":\"Draft\"}}"));

            auditLogs.Add(Log(spec.Id, spec.FullName, "PaRequest", req.ID.ToString(),
                "Submit", submitAt,
                $"{{\"Status\":\"Draft\"}}", $"{{\"Status\":\"Submitted\"}}"));
        }

        // Draft requests: just Create
        foreach (var req in requests.Where(r => r.Status == PaStatus.Draft))
        {
            var spec = req.SubmittedByUserID == specialist1.Id ? specialist1 : specialist2;
            auditLogs.Add(Log(spec.Id, spec.FullName, "PaRequest", req.ID.ToString(),
                "Create", baseDate.AddHours(-1),
                null, $"{{\"RequestNumber\":\"{req.RequestNumber}\",\"Status\":\"Draft\"}}"));
        }

        // 2. BeginReview events for requests that are UnderReview / Approved / Denied / Appealed / Expired
        foreach (var req in requests.Where(r => r.Status is PaStatus.UnderReview
            or PaStatus.Approved or PaStatus.Denied or PaStatus.Appealed or PaStatus.Expired))
        {
            var reviewAt = (req.SubmittedAt ?? baseDate).AddDays(1);
            auditLogs.Add(Log(reviewer.Id, reviewer.FullName, "PaRequest", req.ID.ToString(),
                "BeginReview", reviewAt,
                $"{{\"Status\":\"Submitted\"}}", $"{{\"Status\":\"UnderReview\"}}"));
        }

        // 3. Approval decision events
        foreach (var req in requests.Where(r => r.Status == PaStatus.Approved))
        {
            var decisionAt = req.DecisionRenderedAt ?? (req.SubmittedAt ?? baseDate).AddDays(5);
            auditLogs.Add(Log(reviewer.Id, reviewer.FullName, "PaRequest", req.ID.ToString(),
                "Approve", decisionAt,
                $"{{\"Status\":\"UnderReview\"}}",
                $"{{\"Status\":\"Approved\",\"AuthorizationNumber\":\"{req.AuthorizationNumber}\"}}"));
        }

        // 4. Denial decision events
        foreach (var req in requests.Where(r =>
            r.Status is PaStatus.Denied or PaStatus.Appealed))
        {
            var decisionAt = req.DecisionRenderedAt ?? (req.SubmittedAt ?? baseDate).AddDays(7);
            auditLogs.Add(Log(reviewer.Id, reviewer.FullName, "PaRequest", req.ID.ToString(),
                "Deny", decisionAt,
                $"{{\"Status\":\"UnderReview\"}}",
                $"{{\"Status\":\"Denied\"}}"));
        }

        // 5. Appeal filed
        foreach (var req in requests.Where(r => r.Status == PaStatus.Appealed))
        {
            var spec     = req.SubmittedByUserID == specialist1.Id ? specialist1 : specialist2;
            var appealAt = (req.DecisionRenderedAt ?? baseDate).AddDays(5);
            auditLogs.Add(Log(spec.Id, spec.FullName, "PaRequest", req.ID.ToString(),
                "Appeal", appealAt,
                $"{{\"Status\":\"Denied\"}}", $"{{\"Status\":\"Appealed\"}}"));
        }

        // 6. PendingInfo events
        foreach (var req in requests.Where(r => r.Status == PaStatus.PendingInfo))
        {
            var pendingAt = (req.SubmittedAt ?? baseDate).AddDays(2);
            auditLogs.Add(Log(reviewer.Id, reviewer.FullName, "PaRequest", req.ID.ToString(),
                "RequestAdditionalInfo", pendingAt,
                $"{{\"Status\":\"Submitted\"}}", $"{{\"Status\":\"PendingInfo\"}}"));
        }

        // 7. Withdrawn events
        foreach (var req in requests.Where(r => r.Status == PaStatus.Withdrawn))
        {
            var spec       = req.SubmittedByUserID == specialist1.Id ? specialist1 : specialist2;
            var withdrawAt = (req.SubmittedAt ?? baseDate).AddDays(3);
            auditLogs.Add(Log(spec.Id, spec.FullName, "PaRequest", req.ID.ToString(),
                "Withdraw", withdrawAt,
                $"{{\"Status\":\"Submitted\"}}", $"{{\"Status\":\"Withdrawn\"}}"));
        }

        // 8. Expiration events (by SYSTEM)
        foreach (var req in requests.Where(r => r.Status == PaStatus.Expired))
        {
            var expireAt = (req.AuthorizationEndDate ?? baseDate).AddDays(1);
            auditLogs.Add(Log(SystemUserId, "SYSTEM", "PaRequest", req.ID.ToString(),
                "Expire", expireAt,
                $"{{\"Status\":\"Approved\"}}", $"{{\"Status\":\"Expired\"}}"));
        }

        // 9. Login events for demo accounts (covers compliance demo of login auditing)
        var demoLogins = new[]
        {
            (specialist1.Id,  specialist1.FullName, "127.0.0.1"),
            (specialist2.Id,  specialist2.FullName, "127.0.0.2"),
            (reviewer.Id,     reviewer.FullName,    "127.0.0.3"),
            (admin?.Id ?? "", admin?.FullName ?? "", "127.0.0.4"),
            (billing?.Id ?? "", billing?.FullName ?? "", "127.0.0.5"),
        };

        for (int day = 0; day < 30; day++)
        {
            foreach (var (uid, uname, ip) in demoLogins.Where(x => !string.IsNullOrEmpty(x.Item1)))
            {
                if (day % 3 != 0) continue; // every 3 days per user — keeps count reasonable
                auditLogs.Add(new AuditLog
                {
                    UserID        = uid,
                    UserName      = uname,
                    EntityName    = "ApplicationUser",
                    EntityID      = uid,
                    Action        = "Login",
                    IpAddress     = ip,
                    CorrelationId = NextCid(),
                    OccurredAt    = baseDate.AddDays(day).AddHours(8).AddMinutes(day * 3 % 60),
                });
            }
        }

        // 10. Comment audit events
        var commentedReqs = requests.Where(r =>
            r.Status is PaStatus.Submitted or PaStatus.UnderReview or PaStatus.PendingInfo)
            .Take(9).ToList();
        foreach (var req in commentedReqs)
        {
            var spec    = req.SubmittedByUserID == specialist1.Id ? specialist1 : specialist2;
            var commentAt = (req.SubmittedAt ?? baseDate).AddHours(3);
            auditLogs.Add(Log(spec.Id, spec.FullName, "PaComment", req.ID.ToString(),
                "AddComment", commentAt, null, $"{{\"IsInternal\":false}}"));
        }

        // 11. User management events (Admin: create users, change roles)
        if (admin is not null)
        {
            auditLogs.Add(Log(admin.Id, admin.FullName, "ApplicationUser", specialist1.Id,
                "UserCreated", baseDate.AddDays(-5),
                null, $"{{\"Email\":\"{specialist1.Email}\",\"Role\":\"Authorization Specialist\"}}"));
            auditLogs.Add(Log(admin.Id, admin.FullName, "ApplicationUser", reviewer.Id,
                "UserCreated", baseDate.AddDays(-5),
                null, $"{{\"Email\":\"{reviewer.Email}\",\"Role\":\"Payer Reviewer\"}}"));
            auditLogs.Add(Log(admin.Id, admin.FullName, "ApplicationUser", billing?.Id ?? "",
                "UserCreated", baseDate.AddDays(-4),
                null, $"{{\"Email\":\"{billing?.Email}\",\"Role\":\"Billing Manager\"}}"));
        }

        // 12. CSV export audit rows
        if (billing is not null)
        {
            for (int i = 0; i < 5; i++)
            {
                var exportAt = baseDate.AddDays(10 + i * 7);
                auditLogs.Add(Log(billing.Id, billing.FullName, "PaReport", billing.Id,
                    "ExportDownloaded", exportAt,
                    null, $"{{\"fileName\":\"PA_Export_{exportAt:yyyyMMdd}_120000.csv\",\"rowCount\":50}}"));
            }
        }

        _db.AuditLogs.AddRange(auditLogs);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Seeded {Count} audit log rows.", auditLogs.Count);
    }

    // ── Notifications (demo data for notification center) ─────────────────────

    private async Task SeedNotificationsAsync()
    {
        if (await _db.Notifications.AnyAsync()) return;

        var specialist1 = await _userManager.FindByEmailAsync("specialist@pademo.com");
        var specialist2 = await _userManager.FindByEmailAsync("specialist2@pademo.com");

        if (specialist1 is null || specialist2 is null)
        {
            _logger.LogWarning("Skipping Notification seeding — demo users not yet created.");
            return;
        }

        var requests      = await _db.PaRequests.IgnoreQueryFilters().ToListAsync();
        var denialReasons = await _db.DenialReasons.ToListAsync();
        if (requests.Count == 0) return;

        var today       = DateTime.UtcNow.Date;
        var readCutoff  = today.AddDays(-14); // notifications older than 14 days are pre-read
        var notifications = new List<Notification>();

        // DecisionRendered — notify submitting specialist when request is approved or denied
        foreach (var req in requests.Where(r => r.Status is PaStatus.Approved or PaStatus.Denied))
        {
            var createdAt = req.DecisionRenderedAt ?? today.AddDays(-7);
            var isRead    = createdAt.Date < readCutoff;
            var action    = req.Status == PaStatus.Approved ? "approved" : "denied";
            notifications.Add(new Notification
            {
                UserID           = req.SubmittedByUserID,
                PaRequestID      = req.ID,
                Message          = $"{req.RequestNumber} has been {action}.",
                NotificationType = NotificationType.DecisionRendered,
                IsRead           = isRead,
                CreatedAt        = createdAt,
                ReadAt           = isRead ? (DateTime?)createdAt.AddHours(8) : null,
            });
        }

        // AppealWindow — notify specialist of appeal deadline for appealable denials still within window
        foreach (var req in requests.Where(r => r.Status == PaStatus.Denied))
        {
            if (!req.DenialReasonID.HasValue) continue;
            var reason = denialReasons.FirstOrDefault(d => d.ID == req.DenialReasonID.Value);
            if (reason?.IsAppealable != true) continue;

            var decisionAt     = req.DecisionRenderedAt ?? today.AddDays(-7);
            var appealDeadline = decisionAt.AddDays(reason.AppealDeadlineDays);
            if (appealDeadline < today) continue; // deadline already passed — skip

            notifications.Add(new Notification
            {
                UserID           = req.SubmittedByUserID,
                PaRequestID      = req.ID,
                Message          = $"{req.RequestNumber} was denied. Appeal deadline: {appealDeadline:MMM d, yyyy}.",
                NotificationType = NotificationType.AppealWindow,
                IsRead           = false,
                CreatedAt        = decisionAt.AddHours(1),
            });
        }

        // StatusChanged — notify specialist when request moves to PendingInfo (action required)
        foreach (var req in requests.Where(r => r.Status == PaStatus.PendingInfo))
        {
            var createdAt = req.SubmittedAt?.AddDays(2) ?? today.AddDays(-3);
            var isRead    = createdAt.Date < readCutoff;
            notifications.Add(new Notification
            {
                UserID           = req.SubmittedByUserID,
                PaRequestID      = req.ID,
                Message          = $"{req.RequestNumber} requires additional information before review can continue.",
                NotificationType = NotificationType.StatusChanged,
                IsRead           = isRead,
                CreatedAt        = createdAt,
                ReadAt           = isRead ? (DateTime?)createdAt.AddHours(4) : null,
            });
        }

        // ExpirationAlert — approved requests expiring within 30 days
        var alertWindow = today.AddDays(30);
        foreach (var req in requests.Where(r =>
            r.Status == PaStatus.Approved
            && r.AuthorizationEndDate.HasValue
            && r.AuthorizationEndDate.Value.Date >= today
            && r.AuthorizationEndDate.Value.Date <= alertWindow))
        {
            notifications.Add(new Notification
            {
                UserID           = req.SubmittedByUserID,
                PaRequestID      = req.ID,
                Message          = $"Authorization {req.AuthorizationNumber} for {req.RequestNumber} expires {req.AuthorizationEndDate!.Value:MMM d, yyyy}.",
                NotificationType = NotificationType.ExpirationAlert,
                IsRead           = false,
                CreatedAt        = today.AddDays(-1),
            });
        }

        _db.Notifications.AddRange(notifications);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Seeded {Count} notifications.", notifications.Count);
    }
}
