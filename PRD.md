# Product Requirements Document
## Prior Authorization Workflow Tracker

**Version:** 1.0  
**Date:** May 3, 2026  
**Status:** Draft  
**Author:** Engineering Team

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Problem Statement](#2-problem-statement)
3. [Goals and Non-Goals](#3-goals-and-non-goals)
4. [User Personas and Roles](#4-user-personas-and-roles)
5. [Tech Stack](#5-tech-stack)
6. [System Architecture](#6-system-architecture)
7. [Database Schema](#7-database-schema)
8. [Feature Requirements](#8-feature-requirements)
9. [Business Rules and Validation](#9-business-rules-and-validation)
10. [Role-Based Access Control](#10-role-based-access-control)
11. [Audit Logging](#11-audit-logging)
12. [Reporting and Dashboard](#12-reporting-and-dashboard)
13. [Unit Testing Strategy](#13-unit-testing-strategy)
14. [Seed Data and Demo Accounts](#14-seed-data-and-demo-accounts)
15. [Non-Functional Requirements](#15-non-functional-requirements)
16. [UI/UX Guidelines](#16-uiux-guidelines)
17. [Error Handling and Logging](#17-error-handling-and-logging)
18. [Known Limitations](#18-known-limitations)
19. [Future Improvements](#19-future-improvements)
20. [Glossary](#20-glossary)

---

## 1. Executive Summary

The **Prior Authorization Workflow Tracker** is a healthcare-style workflow management application built with **Blazor Server (.NET 9)** and **SQL Server**. It models the real-world prior authorization (PA) process in which authorization specialists, treating providers, billing managers, and administrators collaborate to submit, review, approve, deny, and report on prior authorization requests for medical procedures, medications, and services.

The project demonstrates:
- Role-based access control with multiple actor types
- Multi-step workflow state management
- Business-rule enforcement at the service layer
- Audit logging for compliance traceability
- Operational reporting with SQL-driven aggregates
- A fully unit-tested service layer

This is a portfolio-quality application that is narrow enough to complete end-to-end but complex enough to reflect professional engineering judgment.

---

## 2. Problem Statement

Prior authorization is a mandatory gate in most US health insurance workflows. A provider orders a treatment; the payer (insurer) must approve it before services are rendered or reimbursed. The process involves multiple actors, has strict time constraints, and produces audit trails required for regulatory compliance.

Without a structured tracker:
- Requests get lost or expire silently
- Denials go unappealable past their deadlines
- Billing teams lack visibility into request status
- Providers waste time chasing status updates by phone
- Compliance teams cannot produce complete audit histories

This application replaces that gap with a structured, role-aware, audited workflow system.

---

## 3. Goals and Non-Goals

### Goals

- Provide a complete PA request queue with status lifecycle management
- Enforce role-based access so each actor only sees and does what is permitted
- Track every state transition with a timestamped audit log
- Alert users to expiring and expired authorizations
- Deliver an operational dashboard with request volume, approval rates, and denial breakdowns
- Support appeal tracking for denied requests
- Export reporting data to CSV
- Demonstrate clean architecture: presentation layer, service layer, data layer, and test layer
- Include synthetic seed data sufficient to make the app immediately demonstrable

### Non-Goals

- Full Electronic Health Record (EHR) functionality
- Real payer API integration (e.g., X12 278 EDI)
- HIPAA-compliant PHI storage (this is synthetic demo data only)
- Billing adjudication or claims submission
- Patient portal or patient-facing UI
- Mobile-native application

---

## 4. User Personas and Roles

### 4.1 Authorization Specialist
The primary daily user. Creates PA requests on behalf of providers, monitors the request queue, submits supporting documentation notes, and tracks decisions.

**Key actions:** Submit requests, upload notes, view queue, receive expiration alerts.

### 4.2 Treating Provider
The clinician ordering the service. Can view status of their own patients' requests and submit clinical justification notes. Cannot approve or deny.

**Key actions:** View own request status, add clinical notes, view decision letters.

### 4.3 Billing Manager
Monitors the financial pipeline. Can see approved and denied requests to plan claims submission. Cannot modify workflow state.

**Key actions:** View approved/denied requests, export reports, view denial reason summaries.

### 4.4 Payer Reviewer
Internal or external reviewer who evaluates submitted requests and issues decisions. Can approve, deny, request additional information, or pend a request.

**Key actions:** Review submitted requests, render decisions, record denial reasons and codes.

### 4.5 Administrator
Full system access. Manages user accounts, configures payer rules, views all audit logs, and can manually override workflow states with a required justification.

**Key actions:** User management, audit log access, system configuration, data export.

---

## 5. Tech Stack

| Layer | Choice | Rationale |
|---|---|---|
| Web Framework | ASP.NET Core Blazor Server | Already scaffolded; real-time UI updates over SignalR suit a live queue |
| Runtime | .NET 9 | Current LTS target in the project file |
| Language | C# 13 | Aligns with .NET 9 |
| Database | SQL Server LocalDB or SQL Server Express | Zero-install for dev; production-ready SQL dialect |
| ORM | Entity Framework Core 9 | Code-first migrations; LINQ queries; strongly typed |
| Authentication | ASP.NET Core Identity | Built-in user/role management, password hashing |
| Authorization | Policy-based + Role-based attributes | `[Authorize(Roles="...")]` plus service-layer permission guards |
| Logging | Microsoft.Extensions.Logging + Serilog sink | Structured logging to file and console |
| Testing | xUnit + Moq | Standard .NET testing ecosystem |
| Reporting | SQL views + EF raw queries + CsvHelper | DB-side aggregation, CSV export |
| Validation | FluentValidation | Service-layer validator classes; completely decoupled from UI |
| UI Styling | Bootstrap 5 (already in wwwroot) | Consistent with scaffolded project |
| State | Blazor Server circuit state + scoped services | Avoid redundant HTTP round trips |

---

## 6. System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      Blazor Server (.NET 9)                     │
│                                                                 │
│  ┌─────────────┐   ┌──────────────┐   ┌──────────────────────┐  │
│  │  Razor      │   │  Shared      │   │  Layout / NavMenu    │  │
│  │  Components │   │  Components  │   │  (role-aware nav)    │  │
│  └──────┬──────┘   └──────┬───────┘   └──────────────────────┘  │
│         │                 │                                      │
│  ┌──────▼─────────────────▼──────────────────────────────────┐  │
│  │                   Service Layer                            │  │
│  │  PaRequestService │ WorkflowService │ ReportingService     │  │
│  │  AuditService     │ NotifService    │ UserManagementService│  │
│  └──────────────────────────┬──────────────────────────────  ┘  │
│                             │                                    │
│  ┌──────────────────────────▼──────────────────────────────────┐ │
│  │              Data Access Layer (EF Core)                    │ │
│  │  AppDbContext │ Repository pattern │ SQL Views              │ │
│  └──────────────────────────┬──────────────────────────────── ┘ │
└─────────────────────────────┼───────────────────────────────────┘
                              │
              ┌───────────────▼───────────────┐
              │    SQL Server (LocalDB/Express) │
              │  Tables │ Views │ Indexes       │
              └───────────────────────────────┘
```

### Layer Responsibilities

| Layer | Responsibility |
|---|---|
| **Components (Pages)** | UI rendering, user input collection, calling services, displaying results |
| **Service Layer** | Business rules, workflow transitions, permission enforcement, audit writes, in-app notification delivery; `ExpirationJob` (`IHostedService`) runs nightly to mark expired authorizations and generate alerts (`NotificationService`) |
| **Data Access Layer** | EF Core DbContext, LINQ queries, parameterized raw SQL for reporting views, EF Core code-first migrations |
| **SQL Server** | Persistent storage, indexed queries, views for reporting aggregates |

---

## 7. Database Schema

### 7.1 Core Tables

#### `Users` (via ASP.NET Core Identity `AspNetUsers`)
Extended with:
- `FullName` (nvarchar 200)
- `Department` (nvarchar 100)
- `IsActive` (bit)
- `CreatedAt` (datetime2)

#### `PaRequests`
| Column | Type | Notes |
|---|---|---|
| `ID` | int IDENTITY PK | |
| `RequestNumber` | nvarchar(20) | Unique, human-readable, e.g. `PA-2026-00001` |
| `PatientMrn` | nvarchar(50) | Synthetic MRN (no real PHI) |
| `PatientName` | nvarchar(200) | Synthetic only |
| `PatientDob` | date | |
| `InsurancePlanID` | int FK | |
| `ProviderID` | nvarchar(450) FK | ASP.NET Identity UserId |
| `SubmittedByUserID` | nvarchar(450) FK | |
| `ProcedureCodeID` | int FK | |
| `DiagnosisCode` | nvarchar(10) | ICD-10 code |
| `ClinicalJustification` | nvarchar(max) | |
| `Status` | nvarchar(50) | Enum: `Draft`, `Submitted`, `PendingInfo`, `UnderReview`, `Approved`, `Denied`, `Appealed`, `Expired`, `Withdrawn` |
| `Priority` | nvarchar(20) | `Routine`, `Urgent`, `Emergent` |
| `SubmittedAt` | datetime2 | Nullable; null while request remains in Draft status |
| `DecisionDueDate` | datetime2 | Nullable; calculated on submission from `SubmittedAt` + plan SLA days adjusted for priority |
| `DecisionRenderedAt` | datetime2 | Nullable |
| `ApprovedUnitsRequested` | int | |
| `ApprovedUnitsGranted` | int | Nullable |
| `AuthorizationNumber` | nvarchar(50) | Nullable; assigned on approval |
| `AuthorizationStartDate` | date | Nullable |
| `AuthorizationEndDate` | date | Nullable |
| `DenialReasonID` | int FK | Nullable |
| `DenialNotes` | nvarchar(max) | Nullable |
| `ReviewerUserID` | nvarchar(450) FK | Nullable |
| `CreatedAt` | datetime2 | |
| `UpdatedAt` | datetime2 | |
| `IsDeleted` | bit | Soft delete |
| `RowVersion` | rowversion | EF Core optimistic concurrency token; prevents simultaneous reviewer decisions on the same request |

#### `PaStatusHistory`
| Column | Type | Notes |
|---|---|---|
| `ID` | int IDENTITY PK | |
| `PaRequestID` | int FK | |
| `FromStatus` | nvarchar(50) | |
| `ToStatus` | nvarchar(50) | |
| `ChangedByUserID` | nvarchar(450) FK | |
| `ChangeReason` | nvarchar(500) | |
| `ChangedAt` | datetime2 | |

#### `PaDocuments`
| Column | Type | Notes |
|---|---|---|
| `ID` | int IDENTITY PK | |
| `PaRequestID` | int FK | |
| `DocumentType` | nvarchar(100) | e.g. `ClinicalNote`, `LabResult`, `ImageReport` |
| `FileName` | nvarchar(500) | |
| `ContentType` | nvarchar(100) | MIME type (e.g. `application/pdf`); required for correct HTTP download headers |
| `FileSizeBytes` | bigint | Stored at upload time; used to enforce size limits at the service layer |
| `StoragePath` | nvarchar(1000) | File system or blob path (metadata only in v1; no file bytes persisted) |
| `UploadedByUserID` | nvarchar(450) FK | |
| `UploadedAt` | datetime2 | |

#### `PaComments`
| Column | Type | Notes |
|---|---|---|
| `ID` | int IDENTITY PK | |
| `PaRequestID` | int FK | |
| `CommentText` | nvarchar(max) | |
| `IsInternalOnly` | bit | Internal = not visible to Provider role |
| `AuthoredByUserID` | nvarchar(450) FK | |
| `AuthoredAt` | datetime2 | |

#### `ProcedureCodes`
| Column | Type | Notes |
|---|---|---|
| `ID` | int IDENTITY PK | |
| `Code` | nvarchar(10) | CPT code |
| `Description` | nvarchar(500) | |
| `RequiresPriorAuth` | bit | |
| `TypicalAuthorizationDurationDays` | int | Expected approved authorization duration in days; used to pre-populate `AuthorizationEndDate` on approval. Distinct from payer decision SLA, which is owned by `InsurancePlans`. |
| `IsActive` | bit | |

#### `InsurancePlans`
| Column | Type | Notes |
|---|---|---|
| `ID` | int IDENTITY PK | |
| `PlanName` | nvarchar(200) | |
| `PayerName` | nvarchar(200) | |
| `PlanType` | nvarchar(50) | `HMO`, `PPO`, `EPO`, `Medicare`, `Medicaid` |
| `RoutineDecisionDays` | int | |
| `UrgentDecisionDays` | int | |
| `EmergentDecisionDays` | int | Default 1 (24 hours); makes BR-002 configurable per plan rather than hardcoded |
| `PhoneNumber` | nvarchar(20) | |
| `FaxNumber` | nvarchar(20) | |
| `IsActive` | bit | |

#### `DenialReasons`
| Column | Type | Notes |
|---|---|---|
| `ID` | int IDENTITY PK | |
| `Code` | nvarchar(20) | e.g. `MED-NOT-NECESSARY` |
| `Description` | nvarchar(500) | |
| `IsAppealable` | bit | |
| `AppealDeadlineDays` | int | |

#### `AuditLogs`
| Column | Type | Notes |
|---|---|---|
| `ID` | bigint IDENTITY PK | |
| `EntityName` | nvarchar(100) | e.g. `PaRequest` |
| `EntityID` | nvarchar(100) | |
| `Action` | nvarchar(100) | e.g. `StatusChanged`, `DocumentUploaded` |
| `OldValues` | nvarchar(max) | JSON snapshot |
| `NewValues` | nvarchar(max) | JSON snapshot |
| `UserID` | nvarchar(450) | |
| `UserName` | nvarchar(200) | Denormalized for log durability |
| `IpAddress` | nvarchar(45) | |
| `OccurredAt` | datetime2 | |
| `CorrelationId` | nvarchar(50) | Nullable; links to Serilog structured log entry for cross-referencing errors |

#### `Notifications`
| Column | Type | Notes |
|---|---|---|
| `ID` | int IDENTITY PK | |
| `UserID` | nvarchar(450) FK | Recipient (AspNetUsers) |
| `PaRequestID` | int FK | Nullable; related request |
| `Message` | nvarchar(500) | Human-readable notification text |
| `NotificationType` | nvarchar(50) | `ExpirationAlert`, `StatusChanged`, `AppealWindow`, `DecisionRendered` |
| `IsRead` | bit | |
| `CreatedAt` | datetime2 | |
| `ReadAt` | datetime2 | Nullable |

### 7.2 Reporting Views

```sql
-- vw_PaRequestSummary
-- Joins PaRequests, ProcedureCodes, InsurancePlans, Users (provider + reviewer)
-- Flattens into a single reportable row per request

-- vw_DenialsByReason
-- Groups denials by DenialReasonId with counts and percentages

-- vw_RequestVolumeByMonth
-- Groups submissions by year-month and status

-- vw_ExpiringAuthorizations
-- Approved requests where AuthorizationEndDate falls within the configurable alert window (default 30 days)
```

### 7.3 Recommended Indexes

| Index Name | Table | Columns | Type |
|---|---|---|---|
| `IX_PaRequests_Status` | PaRequests | Status | Non-clustered |
| `IX_PaRequests_PatientMrn` | PaRequests | PatientMrn | Non-clustered |
| `IX_PaRequests_DecisionDueDate` | PaRequests | DecisionDueDate | Non-clustered |
| `IX_PaRequests_SubmittedByUserID` | PaRequests | SubmittedByUserID | Non-clustered |
| `IX_PaStatusHistory_PaRequestID` | PaStatusHistory | PaRequestID | Non-clustered |
| `IX_AuditLogs_OccurredAt` | AuditLogs | OccurredAt | Non-clustered |
| `IX_AuditLogs_EntityID_EntityName` | AuditLogs | EntityID, EntityName | Non-clustered composite |
| `IX_Notifications_UserId_IsRead` | Notifications | UserId, IsRead | Non-clustered composite |

---

## 8. Feature Requirements

### 8.1 PA Request Submission (FR-001)

- Authorization Specialist can create a new PA request via a multi-section form
- Specialist may save a partial form as **Draft** at any point; minimum fields for Draft save: `PatientMrn`, `InsurancePlanID`, `ProcedureCodeID`; all remaining required fields are enforced only on final submission
- Required fields on submission: patient info, insurance plan, procedure code, diagnosis code, clinical justification, priority, units requested
- System auto-generates a unique `RequestNumber` in format `PA-YYYY-NNNNN`
- System calculates `DecisionDueDate` from `SubmittedAt` + plan SLA days (adjusted for priority)
- On submission, status transitions from `Draft` → `Submitted`
- Audit log entry created for submission event

### 8.2 PA Request Queue (FR-002)

- Displays all requests visible to the current user's role (see RBAC section)
- Filterable by: status, priority, payer, date range, procedure code, assigned reviewer
- Sortable by: submission date, decision due date, patient name, priority
- Rows color-coded by status (e.g., red = Denied/Expired, yellow = PendingInfo, green = Approved)
- Pagination (25 per page default)
- "Due today" and "Overdue" badges on decision due date column

### 8.3 PA Request Detail View (FR-003)

- Full request details including all fields from `PaRequests`
- Status timeline showing all transitions from `PaStatusHistory`
- Comment thread (role-filtered: internal comments hidden from Provider)
- Attached documents list with upload button (for authorized roles)
- Action buttons rendered only for permitted transitions (see workflow matrix below)

### 8.4 Workflow State Machine (FR-004)

Valid status transitions:

```
Draft          → Submitted       (by: Specialist)
Submitted      → UnderReview     (by: Reviewer)
Submitted      → PendingInfo     (by: Reviewer)
PendingInfo    → Submitted       (by: Specialist — after adding info)
UnderReview    → Approved        (by: Reviewer)
UnderReview    → Denied          (by: Reviewer)
UnderReview    → PendingInfo     (by: Reviewer)
Approved       → Expired         (by: System — automated nightly job)
Denied         → Appealed        (by: Specialist — within appeal window)
Appealed       → UnderReview     (by: Reviewer)
Appealed       → Denied          (by: Reviewer — appeal upheld)
Appealed       → Approved        (by: Reviewer — appeal overturned)
Submitted / PendingInfo / UnderReview / Appealed → Withdrawn (by: Specialist or Provider; Draft requests should be deleted, not withdrawn; BR-009 prohibits withdrawing Denied requests)
```

Each transition writes a row to `PaStatusHistory` and an entry to `AuditLogs`.

### 8.5 Decision Rendering (FR-005)

When a Reviewer approves:
- Must enter `ApprovedUnitsGranted`, `AuthorizationStartDate`, `AuthorizationEndDate`
- System generates `AuthorizationNumber` in format `AUTH-YYYYMMDD-XXXXX`
- Email notification stub (logged, not actually sent in v1) sent to submitting Specialist

When a Reviewer denies:
- Must select `DenialReasonId` from lookup
- Optional `DenialNotes`
- If `IsAppealable = true`, appeal deadline is calculated and displayed to Specialist

### 8.6 Expiration Alerts (FR-006)

- Background service (`ExpirationJob`, implements `IHostedService`) runs daily at midnight
- System-initiated expiration transitions record `ChangedByUserID` as a reserved seed identity (username `SYSTEM`); this user is created at startup, is not assignable to human accounts, and exists solely for audit traceability
- Marks approved requests as `Expired` if `AuthorizationEndDate < today`
- Generates in-app notification records for the submitting Specialist and the assigned Provider
- Dashboard shows count of authorizations expiring within 30 days

### 8.7 Appeal Tracking (FR-007)

- Specialists can appeal denied requests if within `AppealDeadlineDays`
- Appeal button disabled and labeled "Appeal window closed" after deadline
- Appealed requests re-enter reviewer queue with `[APPEAL]` badge
- Reviewer can approve (overturn) or deny (uphold) the appeal
- Both outcomes recorded in `PaStatusHistory`

### 8.8 Admin: User Management (FR-008)

- Admin can create, deactivate, and change roles for user accounts
- Admin can view full audit log with filter by user, entity, date range, action type
- Admin can trigger the expiration check manually via a button (for demo purposes)
- Admin can view all PA requests regardless of provider assignment

### 8.9 Dashboard and Reporting (FR-009)

See [Section 12](#12-reporting-and-dashboard) for full specification.

### 8.10 CSV Export (FR-010)

- Billing Manager and Admin can export the current filtered request list to CSV
- Export columns: RequestNumber, PatientName, PatientMrn, ProcedureCode, ProcedureDescription, InsurancePlan, Payer, Status, SubmittedAt, DecisionDueDate, DecisionRenderedAt, AuthorizationNumber, DenialReason, SubmittedByUser, ReviewerUser
- File named `PA_Export_YYYYMMDD_HHmmss.csv`

---

## 9. Business Rules and Validation

### 9.1 Request Submission Rules

| Rule ID | Rule |
|---|---|
| BR-001 | `ProcedureCodes.RequiresPriorAuth` must be `true`. If false, system rejects submission with a clear message. |
| BR-002 | `DecisionDueDate` = `SubmittedAt` + `InsurancePlans.RoutineDecisionDays` for Routine priority; `InsurancePlans.UrgentDecisionDays` for Urgent; `InsurancePlans.EmergentDecisionDays` for Emergent. All SLA values are plan-specific; no durations are hardcoded in application logic. |
| BR-003 | `DiagnosisCode` must match regex `^[A-Z][0-9]{2}(\.[0-9A-Z]{1,4})?$` (ICD-10 format). |
| BR-004 | `ApprovedUnitsRequested` must be between 1 and 999. |
| BR-005 | A patient cannot have two concurrently active (Submitted, PendingInfo, UnderReview, or Appealed) requests for the same `ProcedureCodeID` and `InsurancePlanID`. Checking by payer name alone is insufficient — a patient may hold multiple plans from the same payer. |
| BR-006 | `AuthorizationEndDate` must be after `AuthorizationStartDate`. |
| BR-007 | `ApprovedUnitsGranted` cannot exceed `ApprovedUnitsRequested`. |
| BR-008 | Appeals can only be filed within `DenialReasons.AppealDeadlineDays` of `DecisionRenderedAt`. |
| BR-009 | A request cannot be Withdrawn once it is in `Approved`, `Expired`, or `Denied` status (must appeal instead). |
| BR-010 | Clinical justification must be at least 50 characters. |
| BR-011 | `AuthorizationStartDate` must be ≥ the current date at the time of the approval decision. |
| BR-012 | `ApprovedUnitsGranted` must be ≥ 1; a zero-unit approval is not a valid decision. |
| BR-013 | When re-submitting a request from `PendingInfo` → `Submitted`, at least one new comment or document must have been added after the request entered `PendingInfo` status. Re-submission without new supporting content is rejected with a descriptive error. |
| BR-014 | Draft requests may be permanently deleted (hard delete) only by the Specialist who created them (`SubmittedByUserID`) or by an Administrator. All other status values use soft delete (`IsDeleted = true`) only. |

### 9.2 Field-Level Validation

All validation implemented via `FluentValidation` or data annotations at the service layer, not just the UI layer, so API-level calls are also protected.

---

## 10. Role-Based Access Control

### 10.1 Permission Matrix

| Feature | Specialist | Provider | Billing Manager | Payer Reviewer | Admin |
|---|---|---|---|---|---|
| Submit new PA request | ✓ | | | | ✓ |
| View own patients' requests | ✓ | ✓ (own) | | | ✓ |
| View all requests | ✓ | | ✓ | ✓ | ✓ |
| Add comments (internal) | ✓ | | | ✓ | ✓ |
| Add comments (external) | ✓ | ✓ | | ✓ | ✓ |
| Upload documents | ✓ | ✓ | | ✓ | ✓ |
| Approve / Deny | | | | ✓ | ✓ |
| Request additional info | | | | ✓ | ✓ |
| File appeal | ✓ | | | | ✓ |
| Withdraw request | ✓ | ✓ | | | ✓ |
| Export to CSV | | | ✓ | | ✓ |
| View dashboard | ✓ | | ✓ | ✓ | ✓ || View / dismiss notifications | ✓ | ✓ | ✓ | ✓ | ✓ || View audit log | | | | | ✓ |
| Manage users | | | | | ✓ |
| Override workflow state | | | | | ✓ |

### 10.2 Implementation

- ASP.NET Core Identity roles stored in `AspNetRoles` / `AspNetUserRoles`
- Blazor components check `AuthenticationStateProvider` for role membership
- Service layer re-validates role permissions on every mutating operation (defense in depth)
- Navigation menu items hidden for unauthorized roles using `<AuthorizeView Roles="...">`

---

## 11. Audit Logging

Every state-changing operation writes to `AuditLogs`. The `AuditService` is injected into all services.

### Logged Events

| Event | EntityName | Action |
|---|---|---|
| Request created | PaRequest | Created |
| Status transition | PaRequest | StatusChanged |
| Decision rendered | PaRequest | DecisionRendered |
| Comment added | PaComment | Created |
| Document uploaded | PaDocument | Uploaded |
| Appeal filed | PaRequest | AppealFiled |
| Request withdrawn | PaRequest | Withdrawn |
| Request expired (system) | PaRequest | Expired |
| User created/deactivated | User | Created / Deactivated |
| Role changed | User | RoleChanged |
| Admin override | PaRequest | AdminOverride |
| User login | User | Login |
| Failed login attempt | User | FailedLogin |
| CSV export downloaded | PaReport | ExportDownloaded |

### Audit Log Schema Design Notes

- `OldValues` and `NewValues` are JSON-serialized snapshots of only the changed fields
- Logs are append-only; no update or delete operations on `AuditLogs` are permitted at the application layer
- Logs record `IpAddress` from `IHttpContextAccessor` for traceability

---

## 12. Reporting and Dashboard

### 12.1 Dashboard Page (role-aware)

**Specialist view:**
- My open requests count (by status)
- Requests past decision due date
- Authorizations expiring in next 30 days
- Recent activity feed (last 10 status changes on my requests)

**Billing Manager view:**
- Total approved this month
- Total denied this month
- Denial rate % (denied / total decided)
- Pending requests count
- Top 5 denial reasons (bar chart or table)

**Provider view:**
- My patients’ open request count by status
- Requests in PendingInfo status requiring my clinical input
- Recently approved authorizations expiring within 30 days

**Payer Reviewer view:**
- My assigned requests count
- Requests pending > 5 days
- Average decision time (days) this month

**Admin view:**
- All of the above in combined form
- User activity summary
- Audit log quick-search widget

### 12.2 Reporting Page

| Report | Description | Filter Options |
|---|---|---|
| Request Volume by Month | Submissions per month, by status breakdown | Date range, Payer, Priority |
| Denial Analysis | Count and % by denial reason code | Date range, Payer, Procedure code |
| Approval Rate | Approved vs. denied vs. pending over time | Date range |
| Expiring Authorizations | Approved auths expiring in configurable window | Days-ahead window |
| Turnaround Time | Average days from Submitted → Decision | Date range, Priority |
| Provider Submission Volume | Count of submissions per provider | Date range |

All reports are backed by SQL views or parameterized EF Core queries with `AsNoTracking()`.

---

## 13. Unit Testing Strategy

### 13.1 Test Projects

| Project | Framework | Purpose |
|---|---|---|
| `Tests.Unit` | xUnit + Moq | Service layer logic, workflow rules, validation |
| `Tests.Integration` *(future)* | xUnit + Testcontainers (SQL Server) | Full repository and SQL view integration against a real SQL Server instance; EF Core InMemory is explicitly avoided as it does not enforce FK constraints, does not support raw SQL, and cannot execute database views |

### 13.2 Test Coverage Targets

| Layer | Target Coverage |
|---|---|
| WorkflowService | ≥ 90% — every valid and invalid transition |
| PaRequestService | ≥ 80% — submission, update, appeal |
| BusinessRuleValidator | 100% — all BR-xxx rules |
| AuditService | ≥ 70% — event capture |
| ReportingService | ≥ 60% — query correctness |

### 13.3 Key Unit Test Scenarios

```
WorkflowService_TransitionStatus_ValidTransition_ShouldSucceed
WorkflowService_TransitionStatus_InvalidTransition_ShouldThrowWorkflowException
WorkflowService_Approve_ShouldGenerateAuthorizationNumber
WorkflowService_Deny_NonAppealableReason_ShouldNotAllowAppeal
WorkflowService_Appeal_PastDeadline_ShouldThrowBusinessRuleException
NotificationService_ExpirationAlert_ShouldCreateNotificationForSpecialistAndProvider
BusinessRuleValidator_InvalidIcd10Code_ShouldFailValidation
BusinessRuleValidator_GrantedUnitsExceedRequested_ShouldFailValidation
AuditService_LogStatusChange_ShouldPersistOldAndNewValues
PaRequestService_GenerateRequestNumber_ShouldFollowFormat
WorkflowService_ConcurrentDecision_ShouldThrowDbUpdateConcurrencyException
BusinessRuleValidator_DuplicateActiveProcedureSamePlan_ShouldFailValidation
WorkflowService_Withdraw_FromDraftStatus_ShouldThrowBusinessRuleException
WorkflowService_Approve_ZeroUnitsGranted_ShouldThrowBusinessRuleException
BusinessRuleValidator_AuthorizationStartDateInPast_ShouldFailValidation
```

### 13.4 Mocking Strategy

- `IAppDbContext` or repositories mocked with Moq
- `IAuditService` mocked in service tests (verify interaction, not persistence)
- `ICurrentUserService` mocked to simulate different role contexts
- Clock abstracted behind `IDateTimeProvider` for deterministic expiry tests

---

## 14. Seed Data and Demo Accounts

### 14.1 Demo User Accounts

| Username | Password | Role | Full Name |
|---|---|---|---|
| `specialist@pademo.com` | `Demo@1234` | Authorization Specialist | Sarah Chen |
| `provider@pademo.com` | `Demo@1234` | Treating Provider | Dr. Marcus Webb |
| `billing@pademo.com` | `Demo@1234` | Billing Manager | Angela Torres |
| `reviewer@pademo.com` | `Demo@1234` | Payer Reviewer | James Hollis |
| `admin@pademo.com` | `Demo@1234` | Administrator | System Admin |

### 14.2 Seed Data Volume

| Entity | Count |
|---|---|
| Insurance Plans | 8 (mix of HMO, PPO, Medicare, Medicaid) |
| Procedure Codes | 25 common CPT codes requiring PA |
| Denial Reasons | 12 standard denial reason codes |
| PA Requests | 50 synthetic requests across all statuses |
| PA Status History rows | ~150 (avg 3 transitions per request) |
| PA Comments | ~80 |
| Audit Log rows | ~300 |

> **Note:** Seed data must include at least **3 distinct Provider accounts** and **2 distinct Specialist accounts** so that per-provider and per-specialist reporting shows varied distributions rather than all 50 requests belonging to one user.

### 14.3 Status Distribution in Seed Data

| Status | Count |
|---|---|
| Draft | 3 |
| Submitted | 6 |
| PendingInfo | 5 |
| UnderReview | 7 |
| Approved | 15 |
| Denied | 7 |
| Appealed | 3 |
| Withdrawn | 2 |
| Expired | 2 |

---

## 15. Non-Functional Requirements

| ID | Category | Requirement |
|---|---|---|
| NFR-001 | Performance | Queue page must load ≤ 2 seconds with 50 seed records on LocalDB; paginated list must remain ≤ 3 seconds at ≈500 records (a realistic 12-month single-practice dataset) |
| NFR-002 | Performance | All DB queries must use `AsNoTracking()` for read-only operations |
| NFR-003 | Security | Passwords hashed via ASP.NET Core Identity (PBKDF2-SHA256, 100,000 iterations per .NET 9 defaults — not bcrypt) |
| NFR-004 | Security | All input sanitized; no raw SQL string concatenation (use parameterized queries only) |
| NFR-005 | Security | Anti-forgery tokens enabled on all state-changing form submissions |
| NFR-006 | Security | Unauthorized access attempts logged to Serilog |
| NFR-007 | Availability | Graceful error pages for unhandled exceptions; no stack traces exposed in UI |
| NFR-008 | Accessibility | Bootstrap semantic HTML; all form fields have `<label>` associations |
| NFR-009 | Maintainability | No business logic in Razor components; all in service layer |
| NFR-010 | Maintainability | EF Core migrations used for all schema changes; no manual SQL DDL in startup |
| NFR-011 | Reliability | Blazor Server SignalR circuit must handle transient disconnections gracefully; in-progress form state must be preserved in component state across reconnect attempts; users must see a reconnecting indicator rather than a blank screen |

---

## 16. UI/UX Guidelines

### 16.1 Layout

- Persistent left sidebar `NavMenu` with role-filtered links
- Breadcrumb trail on all detail pages
- Toasts (Bootstrap) for success/error feedback on actions
- Confirmation modal for destructive actions (Withdraw, Deny)

### 16.2 Status Color Coding

| Status | Badge Color |
|---|---|
| Draft | Secondary (gray) |
| Submitted | Primary (blue) |
| PendingInfo | Warning (yellow) |
| UnderReview | Info (cyan) |
| Approved | Success (green) |
| Denied | Danger (red) |
| Appealed | Purple (custom badge-appealed class — #6f42c1) |
| Expired | Dark (black) |
| Withdrawn | Light (white background, gray border — visually distinct from Draft’s filled secondary badge) |

### 16.3 Key Pages

| Route | Page | Roles |
|---|---|---|
| `/` | Dashboard | All authenticated |
| `/requests` | PA Request Queue | All authenticated |
| `/requests/new` | Submit New Request | Specialist, Admin |
| `/requests/{id}` | Request Detail | Role-filtered |
| `/reports` | Reporting | Specialist, Billing, Admin, Reviewer |
| `/admin/users` | User Management | Admin |
| `/admin/audit-log` | Audit Log Viewer | Admin |
| `/notifications` | Notification Center | All authenticated |
| `/requests/{id}/edit` | Edit Request (Draft or PendingInfo only) | Specialist, Admin |

---

## 17. Error Handling and Logging

### 17.1 Exception Hierarchy

```
PaException (base)
├── WorkflowTransitionException   — invalid state transition attempted
├── BusinessRuleViolationException — BR-xxx rule failed
├── AuthorizationException        — role-level access denied
└── EntityNotFoundException       — requested entity does not exist
```

### 17.2 Serilog Configuration

- Minimum level: `Information` in production, `Debug` in development
- Sinks: Console (dev), rolling file `logs/pa-tracker-{Date}.log`
- Enriched with: `{MachineName}`, `{ThreadId}`, `{SourceContext}`
- Error-level log on every caught `PaException`
- Critical-level log on unhandled exceptions

### 17.3 User-Facing Errors

- Validation failures: inline field error messages
- Business rule violations: alert banner with plain-language description
- Unexpected errors: generic "Something went wrong" message + support reference ID (logged correlation ID)

---

## 18. Known Limitations

- **No real file storage:** Document uploads store metadata only; files are not persisted to disk or blob storage in v1.
- **Email notifications are stubs:** The notification system logs intended emails but does not send them. No SMTP configuration is required.
- **No HIPAA compliance:** All patient data is entirely synthetic. This application is not intended for use with real patient health information.
- **No multi-tenancy:** The app assumes a single organization/payer. Multi-tenant isolation is not implemented.
- **No EDI/API integration:** Payer decisions are entered manually by the Reviewer role. No X12 278 or FHIR PAS integration is included.
- **Background job simplicity:** The expiration check uses `IHostedService` with a daily timer. No durable job queue (Hangfire, Quartz) is used.
- **Single reviewer queue:** Requests are not formally assigned to specific reviewers; any Reviewer can act on any Submitted/UnderReview request.

---

## 19. Future Improvements

1. **Durable background jobs** — Replace `IHostedService` timer with Hangfire for reliable scheduling, retries, and a job dashboard.
2. **Real document storage** — Integrate Azure Blob Storage or a local file system mount with virus scanning.
3. **FHIR PAS integration** — Implement HL7 FHIR Prior Authorization Support (PAS) IG for real payer connectivity.
4. **Formal reviewer assignment** — Add round-robin or skill-based routing to assign requests to specific reviewers.
5. **Email and SMS notifications** — Integrate SendGrid or Azure Communication Services for real notifications.
6. **Multi-tenancy** — Add an `Organization` dimension to support multiple payer/provider networks in one deployment.
7. **Advanced reporting** — Replace tabular reports with an embedded charting library (e.g., Chart.js via JS interop) and drill-down capability.
8. **Mobile-responsive enhancement** — Test and refine Bootstrap grid breakpoints for tablet and phone use by specialists in clinical settings.
9. **RBAC fine-grained permissions** — Replace role-string checks with a claims-based permission system (`CanApprove`, `CanExport`, etc.) for more flexible access control.
10. **Integration test suite** — Add `Tests.Integration` project using Testcontainers (SQL Server) for full integration tests that exercise database views, FK constraints, and raw SQL queries that EF Core InMemory cannot support.

---

## 20. Glossary

| Term | Definition |
|---|---|
| **PA / Prior Authorization** | A payer requirement that a provider obtain approval before delivering a service to ensure medical necessity and coverage |
| **CPT Code** | Current Procedural Terminology code; identifies medical procedures for billing |
| **ICD-10** | International Classification of Diseases, 10th revision; diagnosis coding standard |
| **Authorization Number** | Payer-issued reference number confirming an approved PA; required on claims |
| **Denial Reason Code** | Standardized code explaining why a PA was denied |
| **Appeal** | A formal request to reconsider a denied PA |
| **SLA** | Service Level Agreement; the payer's contractual deadline to issue a decision |
| **MRN** | Medical Record Number; patient identifier within a provider's system |
| **EDI X12 278** | Electronic transaction standard for prior authorization requests between providers and payers |
| **FHIR PAS** | HL7 FHIR Prior Authorization Support implementation guide; modern API-based PA standard |
| **Audit Log** | Append-only record of all state-changing events for compliance and traceability |
| **Soft Delete** | Marking a record `IsDeleted = true` rather than removing it from the database |
| **PendingInfo** | Workflow status indicating a payer reviewer has requested additional clinical information; the submitting Specialist or Provider must respond before the review can continue |
| **Turnaround Time** | Elapsed calendar days from a request reaching `Submitted` status to a final decision (`Approved` or `Denied`) being rendered; a key operational KPI tracked in the Turnaround Time report |
| **Withdrawn** | Terminal workflow status indicating a Specialist or Provider has voluntarily cancelled an in-progress PA request before any decision was rendered |
