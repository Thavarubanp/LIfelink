# LifeLink: approved plan, Phases 1–5

Approved on 2026-10-03. This file is the reference for the work. It includes every final decision: the defaults for Q1–Q21 plus the owner's changes.

How the work is done:

- One phase at a time, with a report after each phase.
- Every rule is enforced in the **backend**, not only in the UI.
- The existing race protection (concurrency tokens, 409s, idempotency keys) is kept on everything touched.
- The UI works in light and dark mode, on mobile and desktop.
- Only new or affected things are tested.
- Build, lint and tests run once at the end of each phase. The known failing test `Admin_Dashboard_Statistics_Calculates_All_9_Metrics` is ignored.
- `lifelink.readme.md` is updated at the end of each phase.
- **No commit, no push, no branch changes.** The owner does all git work.
- Never stop or touch the owner's own backend or Vite. Test servers run on separate ports: the backend on 5232, built into a scratch folder, and Vite on 5180.

Test data and email:

- Test data uses clearly fake names and `@example.test` emails. Every record created is listed in the phase report.
- The development database is **not empty**: on 2026-10-03 it held 10 users, 4 hospitals, 2 requests, 34 packets and more. Tests never touch those records.
- No real emails. The test backend runs with `Smtp__Username=YOUR_GMAIL_ADDRESS`, so `MailKitEmailService` skips sending. It also runs with `InventoryMonitoring__Enabled=false`, so it sends no inventory alerts to real hospitals.

---

## 1. Final decisions

### 1.1 The owner's changes to the defaults

| Topic | Final decision |
|---|---|
| **Q7: admin suspension of a request** | Everything is blocked **except two actions**: a donor **can still withdraw** their acceptance, and the creator **can still delete** the request (soft delete). A request that is both deleted and suspended shows **both** statuses to the admin and is recorded in the activity log. When a suspended request is deleted, the **admin gets an in-app notification**. |
| **Q10: admin "Run inventory analysis" button** | **No.** Only hospital staff can run the analysis. |
| **Inventory agent (5.5 and 6.2)** | The agent analyses the inventory of all hospitals. When a hospital's blood group is **below its threshold**, it sends notifications for the **exact blood group only** (no compatible groups) to: (a) the hospital that is low, and (b) the other hospitals that hold Available stock of that exact group, so they can help. **One rule for "below threshold"** is used everywhere (agent, backend and dashboard); Phase 4 states which one. Expiring-soon alerts stay as they are. **No surplus alerts.** In Phase 4, the differences from what the agent does now are reported **before** changing it. |
| **Conflict 11** | Account self-delete and block cleanup stay as they are (the user row is kept; roles, reset tokens and notifications are removed as today). |
| **2.2: admin messages** | One-way announcements. Users and hospitals do not reply. |
| **7.2: editing screening answers** | The agent may re-check the edited answers in the background to build the new report version, as long as the **chat interview does not run again**. |
| **4.5** | The fix is on the donor/patient **Complaints page** (`/donor/complaints`). |
| **Q19** | Yes. Each phase's migration is applied to the development Neon database, which only the owner uses. |
| **Q20** | Yes. No real emails during tests. |
| **Q21** | Yes. The scheduler reads its last scheduled run from `InventoryAnalysisRuns`. |
| **Revised 5.5** | Approved: the `InventoryAnalysisRuns` table and the last-run panel. |
| **Knowing overrides** | Exact-group alerts **replace** the old "compatibility" rule for donor alerts (agentic constraint 6). Soft delete **replaces** the hard delete approved on 2026-09-28. |

### 1.2 Q1–Q21 with their final answers

| # | Question | Final answer |
|---|---|---|
| Q1 | How deleted requests appear | They are hidden from the creator's, hospital's and doctor's lists and from the public list. They stay visible to the admin with a "Deleted" badge (plus "Suspended" if suspended), in activity logs, and in the donor's My Acceptances: the acceptance shows the reason "The request was deleted by its creator", and opening the request shows a read-only "Deleted" banner. They also stay in the doctor's screening history, labelled as deleted. |
| Q2 | The "one active request" unique rule | No change. The partial unique index covers only `Pending`/`Verified`/`Approved`, so a deleted request frees the slot at once. |
| Q3 | Soft-delete scope | Requests and all their child rows, complaints, doctors, inventory groups, and notification dismiss. Technical rows are not included (roles, reset tokens, sessions, idempotency keys, leases), and neither is the account self-delete/block cleanup. |
| Q4 | Deleted doctor display | Keeps "Removed doctor". The email and SLMC number become reusable: the unique indexes are filtered to rows that are not deleted. The doctor's login account is anonymised like a self-delete. |
| Q5 | Contents of a hospital's activity log | Hospital staff actions, plus actions by that hospital's doctors (with the doctor's name), plus admin actions on the hospital. |
| Q6 | Admin messaging | Hospitals are messaged, never doctors (the backend refuses doctor accounts). Messages are one-way, in-app (a notification), and not emailed. Recipients are Active donors/patients and approved, non-suspended hospitals. Suspended accounts are reached through their appeal thread. |
| Q7 | While a request or transfer is admin-suspended | **Changed by the owner, see 1.1.** For requests, donor withdraw and creator delete are allowed and everything else is refused. Suspended requests are hidden from the public list. Held packets stay reserved. The expiry sweep skips suspended requests; if a request is overdue when its suspension is lifted, the next sweep expires it. For transfers, every action is refused while suspended. |
| Q8 | Meaning of the badges in 2.5 | Registrations, appeals and complaints show a **pending** count, which clears when the item is handled. The Activity log shows an **unseen** count, which clears when viewed. |
| Q9 | Rejecting an appeal only once | The Reject button stays disabled even if the appellant replies later. Approve, Close, Reply and Block stay available. |
| Q10 | Admin inventory button | **No.** Hospital staff only. |
| Q11 | Surplus alerts | None. Below-threshold and expiring-soon only. The result toast says "N low-stock, M expiring-soon alert(s) sent". |
| Q12 | Duplicate-alert fix | Yes. A stable dedupe key of hospital + alert type + blood group (`Notifications.DedupeKey`). |
| Q13 | Dashboard low-stock rule | The same single rule as the backend and the agent (fixed in Phase 4) for both the count and the details dialog. |
| Q14 | Scope of "exact group" | Alerts for approved blood requests go only to donors whose saved group equals the request's group. Emergency alerts go only to hospitals holding that exact group. Inventory below-threshold alerts follow the owner's rule in 1.1. Donors can still **accept** a request with a compatible group (the acceptance flow is unchanged). |
| Q15 | LLM answers during screening (7.6) | The knowledge base is used first. If nothing matches, a guarded Gemini answer is given, labelled "General information, not from LifeLink's knowledge base; ask the reviewing doctor", and it never judges eligibility. During the confidential part, only a fixed-topic answer is given. |
| Q16 | Number of screening questions | **7**: 2 personal + 5 health and donation. Consent is part of Q7. If a reply misses parts, one follow-up asks only for the missing parts, and that follow-up is not counted as a new question. |
| Q17 | What the donor sees (7.3) | Only their own answers. Not the AI risk level, flags or summary. The doctor's decision notes are already visible to the donor. |
| Q18 | A donor who doesn't remember (7.5) | Recorded as "Donor doesn't remember", plus an information flag for the doctor (not a deferral). The 120-day yes/no part is still asked inside Q3. |
| Q19 | Migrations | Yes. Applied to the development Neon database, which only the owner uses. |
| Q20 | No real email | Yes (see "Test data and email" above). |
| Q21 | Scheduler state | The scheduler reads its last scheduled run from `InventoryAnalysisRuns` instead of memory. No extra analysis runs right after a restart. |

### 1.3 Conflicts with earlier approved rules (all accepted)

1. **Hard delete (Task 3, 2026-09-28) is replaced by soft delete.** Three earlier delete blocks are dropped: while a donor is active, after a donor was screened ("cancel instead"), and after units were donated. The foreign key from `Acceptances.BloodRequestId` stays. Race safety now comes from the request's concurrency token.
2. **Complaint delete** ("permanently deletes with replies, audit log and reports") becomes a soft delete.
3. **Doctor delete** (lifecycle rule: login removed, "Removed doctor", no column keeping names) becomes a soft delete. The doctor row, and therefore the name, is kept, but the display stays "Removed doctor".
4. **Notification dismiss** ("permanently deletes") becomes a soft dismiss.
5. **Appeal messages alternate between appellant and admin.** The admin may now send several messages in a row; the appellant still waits for an admin message.
6. **Repeated appeal rejection** becomes reject-once per appeal.
7. **Agentic constraint 6** ("donor alerts check compatibility") is replaced by exact-group alerts. This is a knowing override.
8. **Agentic constraint 3** (medical answers cite sources): LLM answers that do not come from the knowledge base are labelled "general information".
9. **Screening Section 10 is confidential and is never sent to an LLM.** This is kept: that part is its own rule-parsed question (Q7).
10. **"Minimal scope: no extra tables or endpoints"** (agentic expansion) is relaxed. This plan adds the tables `ActivityLogs`, `AdminSeenMarkers` and `InventoryAnalysisRuns`, and several endpoints.
11. **Account self-delete and block cleanup** physically remove roles, reset tokens and notifications. **Kept unchanged** (owner's decision).
12. **SLMC unique system-wide** (lifecycle rule, 2026-09-24) is replaced by the owner's Phase 2 decision (see 1.4): SLMC is unique per hospital among doctors who were not removed.

### 1.4 Owner's decisions for Phase 2 (given with "GO", 2026-10-03)

1. Soft delete exists so the **Admin** still sees everything (deleted requests, removed doctors, and so on) for the activity logs. Users and hospitals never see deleted items in their lists, and there is no "show deleted" filter for creators. Removed doctors disappear from the hospital's doctor list and pickers and still show as "Removed doctor" in past history.
2. **Doctor uniqueness:**
   - The doctor email is unique across the whole system among non-deleted doctors (the Users table too), so a removed doctor's email can be reused.
   - The SLMC number is unique only within one hospital among non-deleted doctors: unique index (HospitalId, LicenseNumber) WHERE DeletedAt IS NULL. The same doctor may hold a separate account at another hospital with a different email.
3. The donor's My Acceptances keeps the closed acceptance as "Closed – the request was deleted", without the deleted request's details. Doctor screening reports stay (medical records).
4. Re-adding a deleted blood group restores the row with the new threshold and capacity entered.
5. Request delete: the creator can delete at any status. Active acceptances are closed, held and reserved packets are released, screening reports are kept, and the hospital, the assigned doctor and the accepted donors/hospitals are notified. Anyone else gets 403.
6. Every other hard delete becomes soft (including doctor pending assignments), except the self-delete/block cleanup (Conflict 11) and housekeeping (idempotency keys, reset tokens, role rows).
7. The development database is used only by the owner and all its data is test data. The Phase 2 migration may be applied and a test backend run on its own port. Existing records must not be changed, except my own clearly fake test records.

### 1.5 Owner's decisions for Phase 3 (given with "GO" for 3A, 2026-10-03)

- **Phase 3 is split:**
  - 3A = 2.1 activity logs, 2.4 the admin Activity log page with its unseen highlight, 2.5 badges.
  - 3B (later) = 2.2 one-way admin messages, and 2.3 the admin view of all requests and transfers with Suspend/Lift (the Q7 rules).
- Sign-in and sign-out are **not** recorded for any role. A doctor's first password change is recorded.
- Every activity log view (admin view of user and hospital profiles, `/admin/activity`, the user and hospital "my activity" view) has pagination and a filter by action type and date range.
- Admin transfers tab: the existing `GET /api/transfers` already returns all transfers for the Admin, so no `/api/Admin/transfers` was added.
- No backfill of older actions. The views show "Activity is recorded from 3 Oct 2026".
- The threshold-5 default for automatically created blood groups works now (it is not held until Phase 4).

---

### 1.6 Owner's decisions for 3B, Phase 4 and Phase 5 ("APPROVED", 2026-10-03)

These override the older 3B/4/5 text in section 3 wherever they differ.

- **Q1 Messages:** a one-way **"Message from Administrator"** to ONE user or ONE hospital at a time, sent from their profile. No broadcast, and it is never called an "announcement". Nobody can reply; to contact the admin, users and hospitals file a complaint (change A).
- **Change A, complaints as questions:** the name "Complaints" stays. The target (user or hospital) is **optional**; without one, the complaint is a general question or doubt for the admin, handled on the same admin page with the same reply thread. When a target is given, every existing rule still applies.
- **Q2:** a final tick "I confirm my answers are true" (not counted as a question). A screening report cannot be submitted without it.
- **Q3:** matching expiring-soon packets uses the **exact group**.
- **Q4:** alcohol in the last 24 hours.
- **Q5 (changed):** a hospital is low when `UnitsAvailable < MinimumThreshold`. The "help" alert goes to **all other hospitals whose stock of that EXACT group is above their own threshold** (`UnitsAvailable > MinimumThreshold`). Hospitals at or below their threshold get no help alert.
- **Alert wording:** inventory alerts show actual **unit counts**, never threshold figures or "2/5". Examples:
  - Low hospital: "You have only 2 units of A+ left. Hospitals holding A+: Hospital B (12 units), ..."
  - Help alert: "Hospital A has only 2 units of A+ left. You hold 12 units of A+. Consider offering a transfer."
- **Blood request alerts (Notification agent):**
  - **Normal priority:** no alerts to anyone. Donors find these requests in the public list. The creator, the hospital and the assigned doctor still get their normal status notifications.
  - **Critical / Emergency** only: alert all hospitals holding that EXACT group, and all eligible users whose saved group is EXACTLY that group (120 days, Active, not suspended or blocked). Users without a saved blood group are never alerted.
- **Conflicts:** all 17 section-B resolutions are accepted. Conflict 15: a message to a suspended account is stored and readable after reinstatement; no notification access is added for suspended accounts.
- **Additions:**
  - The malaria country list carries a source comment and is listed under "Decisions to review".
  - The final report explains how to build the Supervisor knowledge base, and builds it if a script exists and needs only the Gemini key.
  - The final report lists the services needed for a demo, with ports and start commands.
- **Process:** implement 3B (with change A) → 4 → 5 in one run. Apply each migration. Update section 5 and the README after each part. Write `docs/cleanup-test-records.sql` (not run). Write one final report.

## 2. Current state found before starting (2026-10-03)

| Item | State |
|---|---|
| 1.1 | Hard deletes: blood requests (with their doctor assignments, acceptances and matches), complaints (with replies and activity reports), doctors (and their login when it has no history), unused inventory groups, and dismissed notifications. Transfers already soft-delete (their status becomes `Cancelled`). |
| 1.2 | Only the creator can delete (403 otherwise). Delete is refused while a donor is active, after a screening, or after a donation. It is a hard delete. |
| 2.1 | There is no general activity log, only scattered traces: `InventoryTransactions`, request verifications, screening report versions, fulfilment history, the registration thread, the complaint audit log, appeal messages and `UserSessions`. |
| 2.2 | There are no direct admin messages. |
| 2.3 | The admin already sees all transfers (`GET /api/transfers`). There is no list of all blood requests for the admin, and requests cannot be suspended. |
| 2.4 / 2.5 | There is no Activity log page and there are no badges. |
| 3.1 | `SuspendHospitalAsync` has no approval check. |
| 3.2 | `AdminReplyAsync` refuses a second admin message in a row. |
| 3.3 | Reject only checks that the thread is open, so an appeal can be rejected again. |
| 4.1 | The light-mode overrides for the auth pages (`.ll-auth-page`) miss several dark-only classes. |
| 4.2 | Registration has no blood group field. |
| 4.3 | Already exists: the profile shows "Blood Group & Eligibility" and its edit dialog can change the blood group. |
| 4.5 | The status badges and the solved/answered complaint card use dark-only colours. |
| 5.1 | `Doctor.MustChangePassword` exists, but `DoctorAssignmentRules` ignores it. |
| 5.2 | There are two cards. Groups created automatically get threshold 0 and capacity 100 (`InventoryLedger.cs`). |
| 5.3 | The dashboard cards are static. The low-stock count uses `units <= (threshold \|\| 5)`, while the backend uses `units <= threshold` and the agent uses `units < threshold`. |
| 5.5 | The analysis only runs on its schedule (details in Phase 4). |
| 7.x | About 50 questions in 12 sections. "Update my answers" re-runs the chat. The donor cannot see their own answers. When the donor asks something, the knowledge base answers and the question is repeated. |

---

## 3. Phases

### Phase 1: Quick UI fixes, registration and profile (4.1, 4.2, 4.3, 4.5)

**Migration:** none.

| Item | Approach | Files |
|---|---|---|
| 4.1 | New light-only overrides, scoped to a new `ll-register-page` class, cover the remaining dark-only classes: white text on slate backgrounds, hover colours, cyan/amber/emerald/red/blue/rose text, and the red/emerald/cyan/blue `-950` boxes. The class is added to the donor register page, the hospital register page and the hospital "waiting for approval" page (the last screen of hospital registration). The login and governance pages are not affected. | `frontend/src/index.css`, `RegisterPage.jsx`, `RegisterHospitalPage.jsx`, `WaitingForApprovalPage.jsx` |
| 4.2 | `RegisterRequestDto` gets an optional `BloodGroup`. It is validated and normalised with `BloodValidationHelper` ("Invalid blood group." → 400) and saved in `AuthService.RegisterAsync`. The form gets a "Blood Group (optional)" select, where "I don't know" sends nothing. | `RegisterRequestDto.cs`, `AuthService.cs`, `RegisterPage.jsx` |
| 4.3 | Already works. Only an "Add your blood group" link is added; it shows when the group is "Not set" and opens the existing edit dialog. | `UserProfilePage.jsx` |
| 4.5 | Status badges, the answered/solved card (dark purple gradient), the admin-response panel, the target and "Admin Replied" badges, the progress labels, the evidence reports and the empty state all get light/dark colour pairs. | `DonorComplaintsPage.jsx` (`ComplaintActivityTimeline.jsx` was checked and is already fine) |

- **Tests:** a backend test for registration with a valid, invalid and missing blood group.
- **UI check:**
  - The register pages (including the error banner, filled form, and the success/waiting/rejected states, using mocked submits), in light/dark × mobile/desktop.
  - The profile and the complaints page.
- **Test data:** one fake donor and one complaint on that donor, marked solved.

### Phase 2: Deletion and governance (1.1, 1.2, 3.1, 3.2, 3.3)

**Migration `SoftDeleteAndAppealReject`:**

- `BloodRequests.DeletedAt`
- `Complaints.DeletedAt`
- `Doctors.DeletedAt`, with the unique indexes on Email and SLMC filtered to rows that are not deleted
- `BloodInventories.DeletedAt`
- `Notifications.DismissedAt`
- `Appeals.RejectedAt`

All columns are nullable; no backfill is needed.

**1.2 Request delete (`DeleteRequestAsync`)** works in any status and only for the creator (403 for anyone else). It returns 409 if the request is already deleted.

In one save:
- The status becomes `Deleted` and `DeletedAt` is set.
- `AcceptanceClosure.CloseAllAsync` cancels the active acceptances with the reason "The request was deleted by its creator". This frees reserved slots, closes pending report versions (which are kept) and releases held packets through the ledger.
- Open matches are set to Cancelled.
- Notifications go to the hospital (unless it is the creator), the assigned doctor, and the donors and donor hospitals whose acceptances were closed.

Other rules:
- Donated (Matched) acceptances, fulfilment history and packets are not touched.
- If a donor accepts at the same moment, they get 409, because adding an acceptance bumps the request's concurrency token.
- Lists (`my`, `hospital`, `assigned`, `pending`, `public`) filter out `Deleted` requests. Details follow Q1.
- **Admin suspension (added in Phase 3):** delete stays allowed while a request is suspended. The admin gets an in-app notification and an activity log entry, and still sees the request as both Deleted and Suspended.
- **UI:** `MyRequestsPage.jsx` always shows Delete. Its confirmation names the active donors who will be notified.

**1.1 Other soft deletes:**
- **Complaint cancel:** sets `DeletedAt`; replies and reports are kept and hidden.
- **Doctor delete:** sets `DeletedAt` and `IsActive = false`. Pending assignments are set to `Closed` instead of being removed, and their requests still go back to Pending. The "assigned doctor" lookup ignores Closed rows. The login is anonymised, and the doctor is hidden from lists.
- **Inventory group delete:** sets `DeletedAt`. Re-adding the same group, or adding packets of it, restores the row.
- **Notification dismiss:** sets `DismissedAt`; the notification is hidden from lists and unread counts.

**Governance:**
- **3.1:** `SuspendHospitalAsync` refuses unless `ApprovalStatus == Approved`. `AdminDashboard` hides the button otherwise.
- **3.2:** only the turn check in `AdminReplyAsync` is removed. The appellant's own turn rule is unchanged.
- **3.3:** Reject sets `RejectedAt`; a second reject returns 409. The DTO gets `canReject`, and `AdminAppealsPage.jsx` disables the button.

**Files:** `BloodRequestService.cs`, `ComplaintService.cs`, `DoctorService.cs`, `BloodInventoryService.cs`, `InventoryLedger.cs` (restore), `NotificationAgentService.cs`, `EmailUniquenessHelper.cs`, `SlmcUniquenessHelper.cs`, `AdminService.cs`, `AppealService.cs`, `AppDbcontext.cs`, the related DTOs and pages, and `BloodRequestDeleteTests` (rewritten for the new rules).

**Tests:**
- Deleting with an active donor, a screened donor, a reserved slot, a held hospital donation, and a Completed request; deleting as someone who is not the creator.
- A delete racing an accept.
- Soft delete of a complaint, a doctor, an inventory group and a notification.
- Suspending a pending hospital (refused).
- Two admin replies in a row; rejecting twice.

**Test data:** a fake approved hospital, a doctor, a patient and a donor; one request taken up to an active screening; one complaint; one suspended donor with an appeal.

### Phase 3: Admin oversight, activity logs, messaging, badges (2.1–2.5, 5.4)

**Migration `ActivityLogsAndOversight`:**

- **`ActivityLogs`** table: `Id`, `OccurredAt`, `ActorUserId`, `ActorRole`, `HospitalId`, `SubjectUserId`, `Action`, `EntityType`, `EntityId`, `Summary`.
  - Indexed by actor, hospital, subject and time.
  - Append-only: `AppDbContext` refuses updates and deletes.
  - Reason: no existing table records who did what and when.
- **`AdminSeenMarkers`** table: `AdminUserId`, `Area`, `SeenAt`. Reason: the "unseen" badges.
- **`BloodRequests` and `HospitalTransferRequests`:** new columns `AdminSuspendedAt`, `AdminSuspensionReason`, `AdminSuspendedById`.

**2.1 Recording.** `ActivityLog.Add(context, …)` is called inside each action's own save, so a failed action logs nothing. Recorded actions:
- **Requests:** create, edit, cancel, delete, verify, approve, reject, record donation, release.
- **Donors:** accept, withdraw, screening submitted or updated.
- **Doctor decisions:** on screening reports and on hospital donations.
- **Packets:** add, edit, issue.
- **Inventory:** groups and thresholds.
- **Transfers and emergencies.**
- **Doctors:** added and removed.
- **Profile edits.**
- **Complaints and appeals.**
- **Sign-in.**
- **Admin actions on a user or hospital:** suspend, reinstate, block, approve, messages, and request/transfer suspensions.
- **The manual inventory run** (Phase 4).

Endpoints:
- `GET /api/activity-logs/my` (User and HospitalStaff, own data, paged)
- `GET /api/Admin/users/{id}/activity-log`
- `GET /api/Admin/hospitals/{id}/activity-log`

Where it shows:
- The admin view of the user and hospital profile pages.
- The end of the donor dashboard (5.4).
- Both use one shared `ActivityLogList` component.

**2.2 Messaging.** `POST /api/Admin/messages {userId | hospitalId, subject, message}` (Admin) creates an `AdminMessage` notification and a log entry. It is a **one-way announcement with no reply**. There is a "Send message" button on the admin view of the user and hospital profiles.

**2.3 Oversight.**
- `GET /api/Admin/blood-requests` lists all requests, including deleted ones.
- Suspend and lift:
  - Requests: `PUT /api/Admin/blood-requests/{id}/suspend` and `/lift`.
  - Transfers: `PUT /api/Admin/transfers/{id}/suspend` and `/lift`.
  - Only open items can be suspended.
- Enforcement:
  - One guard in every action that changes a request or transfer, and in the agent callbacks (screening report and chat turns), returns 409 "temporarily suspended by the administrator".
  - **Exceptions (owner's Q7):** donor withdraw and creator delete.
  - The concurrency token makes a concurrent action fail.
- Notifications on suspend and on lift: the creator, the hospital, the assigned doctor and the active acceptors; for transfers, both hospitals. The admin is also notified when a suspended request is deleted.
- No admin edit or delete endpoints are added.

**2.4 / 2.5 Activity log page and badges.**
- A new page `/admin/activity` ("Activity log" in the sidebar) with tabs for Blood requests and Transfers. New items are highlighted, and each row has Suspend/Lift and a status filter. Opening a tab marks that area as seen.
- `GET /api/Admin/attention-counts` returns the unseen requests and transfers, plus the pending registrations, appeals and complaints. It is polled every 30 seconds as a background request that does not extend the session.
- `PUT /api/Admin/attention/{area}/seen` marks an area as seen.
- Badges appear on the sidebar and on the dashboard quick links.

**Files:**
- New: the `ActivityLog` entity, helper and controller; `AdminActivityPage.jsx`; `ActivityLogList.jsx`; `activityApi.js`.
- Changed: logging calls across `Services/*`, `AdminController`/`AdminService`, `Sidebar.jsx`, the profile pages and `DonorDashboard.jsx`.

**Tests:**
- A log entry is written and rolled back together with its action.
- Log scoping (own data only; the admin sees everything).
- Messaging rules (doctors are refused).
- A suspension blocks every action, including agent callbacks, except withdraw and delete.
- A suspension racing an action.
- Notifications on suspend, on lift, and on deleting a suspended request.
- Badge counts and seen markers.

**Test data:** reuses Phase 2 data, plus a second fake hospital and one transfer between the two fake hospitals.

### Phase 4: Hospital side (5.1, 5.2, 5.3, 5.5)

**Migration:** the table `InventoryAnalysisRuns`, and `Notifications.DedupeKey` with an index (Q12).

**5.1 Doctors pending first login.**
- `RequireAssignableDoctorAsync` refuses a doctor who still has `MustChangePassword`: "This doctor hasn't signed in and changed the temporary password yet." This covers both doctor pickers: creating a hospital request and Verify.
- The pickers hide these doctors, and Doctor Management shows them as "Pending first login".
- **Fallback rule:** it still applies when the assigned doctor has been removed or is inactive. The fallback doctor must also have completed first login; the backend enforces this for report review and hospital donation decisions.
- A hospital with no doctor past first login cannot create or verify requests, and the error message says why.

**5.2 Inventory page.**
- One card for blood groups. Its header holds "Add blood group", "Add packets", the search box and the status filter.
- Each group row has an expand toggle that shows its packets: tracking no., collected, expiry, created by, status, and Edit/Issue/History. The status filter applies inside the expanded rows.
- The existing dialogs, validation and the Thresholds action are reused.
- Groups created automatically get **threshold 5 and capacity 100** (owner's follow-up after Phase 2; existing rows unchanged).
- `?group=A%2B` expands that group and scrolls to it.

**5.3 Hospital dashboard.**
- The unit cards link to `/hospital/inventory?group=…`.
- The Critical emergencies and Low stock cards open detail dialogs. The emergency dialog shows the hospital, group, units, reason and time, with a link to the Emergency Center. The low-stock dialog shows the group, units and threshold, with a link to Inventory.
- The hospital's activity log is shown at the end of the dashboard.

**5.5 Manual inventory analysis (revised and approved).**

*How the scheduled run works today:*
1. `RequestExpiryBackgroundService` runs every 5 minutes, only on the backend instance that holds the lease.
2. Every 30 minutes (`InventoryMonitoring:IntervalMinutes`) it calls `InventoryMonitor.RunInventoryCheckAsync`.
3. That sends `POST /plan InventoryCheck` to the **Supervisor**.
4. The Supervisor calls the Inventory agent (`/analyze`, monitor mode), then the planning step (at most 3 alerts per hospital, duplicates dropped), then the Notification agent (`/hospital-alerts`, where Gemini rewrites the wording).
5. The backend saves alerts only for approved, non-suspended hospitals. If the Supervisor can't be reached, rule-based alerts are sent instead.
6. The Inventory agent's own scheduler is off (`SCHEDULER_ENABLED=False`).

*Duplicate alerts today:* an alert is skipped if an unread alert with the same type and **same title** was sent to that hospital in the last 12 hours. Gemini rewrites titles, which defeats this check. The fix is a `DedupeKey` of hospital + type + blood group. The blood group is passed through the Supervisor and the Notification agent as a plain extra field; their logic does not change.

*Inventory agent's main task (owner's rule).* When a group is below its threshold, the agent sends an exact-group alert to the low hospital and to the other hospitals holding Available stock of that exact group. Expiring-soon alerts stay as they are, and there are no surplus alerts.
- One "below threshold" rule is applied in the agent, the backend low-stock list, the rule-based fallback and the dashboard.
- **Before changing anything, the report lists how this differs from the current agent.** Known today: shortage alerts go only to the low hospital, which is told which hospitals have spare stock. Spare stock currently means `current > threshold`, not just Available stock. Expiring packets are matched to public requests with compatible groups.

*Running it:*
- `POST /api/Inventory/analysis/run` is **for hospital staff only (no admin button)**. It calls the same `InventoryMonitor` code as the scheduled run.
- **Lock and cooldown:** a second `BackgroundJobLeases` row, `InventoryAnalysis`, taken with single atomic statements (safe with the Neon pooler).
  - It can only be taken when free. While running, the holder is `running:<id>`, with a 5-minute safety expiry.
  - At the end the holder becomes `cooldown:<id>` until finish + **2 minutes**. The cooldown is global.
- **Scheduled runs use the same lease.** If a run is in progress or cooling down, the scheduled run is skipped and retried in the next 5-minute round.
- **Responses:**
  - 409 "Analysis is already running"
  - 409 "Analysis ran moments ago — try again in N s"
  - 200 `{lowStockAlerts, expiringAlerts, skippedDuplicates}`
- `RunInventoryCheckAsync` returns counts per alert type.

*Last-run data: the new table `InventoryAnalysisRuns`*, one row per run:

| Column | Meaning |
|---|---|
| `RunId`, `StartedAt`, `FinishedAt` | When the run happened |
| `Trigger` | `Scheduled` or `Manual` |
| `TriggeredByHospitalId`, `TriggeredByUserId` | Who started a manual run |
| `Status` | `Running`, `Completed`, `CompletedRuleBased`, `Failed` |
| `LowStockAlerts`, `ExpiringAlerts`, `SkippedDuplicates` | Result summary |

- The lease row stays the only lock. The table only records what happened.
- A row with no finish time after the safety expiry is shown as "interrupted".
- Rows are never deleted (about 48 a day).
- **Q21:** the scheduler reads its last scheduled run from this table, so the 30-minute schedule holds across restarts and changes of lease holder.

*Status endpoint.* `GET /api/Inventory/analysis/status` (hospital staff) gives the same answer to every hospital:

```
{ state: "Idle" | "Running" | "Cooldown", cooldownEndsAt, serverNow,
  lastRun: { startedAt, finishedAt, trigger, hospitalName, status, lowStockAlerts, expiringAlerts, skippedDuplicates },
  nextScheduledAt, scheduleEnabled }
```

*Panel next to the button, on the hospital dashboard:*

| State | What it shows |
|---|---|
| Idle | "Last analysis: 3 Oct 2026, 2:15 PM (manual by Test Hospital A) – 2 low-stock, 1 expiring-soon alert sent", or "(scheduled)" for a scheduled run, and "Next scheduled run in 18 min" or "due now" |
| No run yet | "No analysis has run yet." |
| Failed / interrupted | That status in the result summary |
| Scheduled runs off | "Scheduled runs are off" |
| Running | "Analysis running…" with a spinner; the button is disabled |
| Cooldown | The button is disabled and shows "Available again in 1:24", counting down every second |

- All times are in **Sri Lanka time** (`Asia/Colombo`). Countdowns use `serverNow`.
- The panel refreshes every 15 seconds in the background (without extending the session) and right after a run.
- On finish, a toast shows "Analysis complete: N low-stock, M expiring-soon alert(s) sent".
- Manual runs also appear in that hospital's activity log.

**Files:** `DoctorAssignmentRules.cs`, `VerificationService.cs`, `AcceptanceService.cs`, `BloodRequestService.cs` (fallback), `InventoryMonitor.cs`, `BackgroundJobLeases.cs`, `RequestExpiryBackgroundService.cs`, `InventoryController.cs`, `NotificationAgentService.cs`, the new `InventoryAnalysisRun` entity, `InventoryManagementPage.jsx`, `HospitalDashboard.jsx` (with the `InventoryAnalysisStatus` component), `DoctorManagementPage.jsx`, and the two doctor pickers. The agent files are listed in Phase 5.

**Tests:**
- Assigning a doctor who is pending first login (refused), and the fallback rules.
- The lease: busy, cooldown and release, including on PostgreSQL.
- Run rows for manual, scheduled, fallback and failed runs, and for a crash (interrupted).
- The status endpoint in the Idle, Running and Cooldown states, and the next-run time.
- The dedupe key.
- Endpoint roles (hospital staff only).
- A UI check of every panel state.

**Test data:** a fake doctor who is still pending first login, and packets in two groups for the fake hospitals.

### Phase 5: Agents and screening (4.4, 6.1, 6.2, 7.1–7.7)

**Every place with compatible-group logic, and what changes:**

| Place | Flow | Change |
|---|---|---|
| `NotificationAgentService.BloodCompatibility` → `GetEligibleDonorCandidatesAsync` | Donor alerts for approved requests (agent path and fallback) | **Exact group** (4.4) |
| `Agents/Notification/nodes.py` `COMPATIBILITY_MATRIX`, and the ranking rule in `prompts.py` | The same donor alerts, inside the agent | **Exact group**, wording adjusted |
| `EmergencyRequestService.CompatibleDonorGroups` (`FindHospitalsWithCompatibleStockAsync`) | Emergency Center hospital alerts | **Exact group** (6.1) |
| `Agents/Notification/app.py` emergency template, `Supervisor/services/planner.py` summary, `EmergencyHubPage.jsx` text | Wording ("compatible units") | Text only |
| `Agents/InventoryManagement/services/analysis_service.py` `COMPATIBLE_DONORS` | InventoryCheck: expiring packets matched to public requests, and shortage alerts (owner's rule) | **Exact group** (6.2). The shortage recipients change in Phase 4. |
| `BloodCompatibilityService` (donor accept in `AcceptanceService`, and the tested-group check when a donation is recorded) | Donor acceptance and donation recording | **Not changed** (Q14) |
| Supervisor `intents.py`, medical knowledge docs | Chat topic detection and education | Not changed. The platform knowledge doc text is updated. |

**7.1** The doctor's view is unchanged: the flags box, the risk level and all sections. The report keeps field-level items.

**7.4–7.7 The final 7 questions:**

1. **Personal (1/2):** confirm or correct full name, date of birth and gender, and give the NIC/passport number.
2. **Personal (2/2):** confirm or correct address, mobile, email and blood group (or "don't know"), and give an emergency contact name and phone. The occupation question is removed.
3. **Donation history:**
   - donated before?
   - if yes: date of last donation ("don't remember" is accepted), roughly how many times, any problems afterwards, have 120 days passed?
   - ever advised not to donate?
4. **Today's health and basic eligibility:**
   - feeling well, a meal in the last 4 hours, slept 6 hours or more
   - fever, cough, cold, sore throat or flu right now
   - under treatment (what), alcohol in the last 24 hours, weight over 50 kg
   - female donors only: pregnant, breastfeeding, abortion in the last 6 months, gave birth in the last 12 months, miscarriage in the last 6 months
5. **Medical history and the last 12 months:**
   - long-term conditions
   - transfusion, surgery, hospital stay, tattoo, piercing, vaccination and similar events
   - recent illnesses, contact with hepatitis, anti-malaria medicines in the last 3 years
   - dental work or medicines, and current medicines
6. **Travel and recent symptoms:**
   - abroad in the last 3 years (countries, malaria areas, dates)
   - in the last 6 months: fever, night sweats, weight loss, diarrhoea, swollen glands, tiredness
7. **Confidential (rules only, never sent to an LLM):** the infection-risk checklist (or "None"), plus confirmation and consent.

How the agent handles the answers:
- Age is worked out from the date of birth.
- For Q1–Q6, Gemini extracts the same field IDs as today, with rule-based parsers as a fallback. The eligibility rules, the report builder and the agent's database schema stay unchanged.
- **7.5:** "forgot / don't remember" is stored as "Donor doesn't remember", plus an information flag for the doctor. The question is never asked again.
- **7.6:** when the donor asks a question, it is answered as in Q15, and then the current question is repeated.

**7.2 Edit form.**
- "Update my answers" opens a form prefilled from the latest report version.
- Saving goes `PUT /api/Acceptances/{id}/screening-answers` (the donor, while the report is still waiting for the doctor) → Supervisor `/plan` event `ScreeningAnswersUpdated` → the Request Management agent's new `PUT /api/agent/screening/answers/{id}`.
- The agent re-checks the answers **in the background** and submits a new version through `report-notify`. The previous version becomes Superseded.
- The **chat interview does not run again.**

**7.3 Donor view.** `GET /api/Acceptances/{id}/screening-answers` (own acceptance only) returns the donor's answers without the AI fields. "View my answers" on My Acceptances uses the same component as the form, read-only.

**Files:**
- Request Management agent: `models/donor_screening.py`, `answer_parser.py`, `screening_service.py`, `graph/workflow.py`, `report_service.py`, `eligibility_rules.py`, `api/routes.py`.
- Supervisor: nodes and routes.
- The Notification and Inventory files in the table above.
- Backend: `AcceptanceService.cs`, `AcceptancesController.cs`, `NotificationAgentService.cs`, `EmergencyRequestService.cs`.
- Frontend: `MyAcceptancesPage.jsx`, a new `ScreeningAnswersForm.jsx`, `EmergencyHubPage.jsx`.

**Tests:**
- pytest:
  - Request Management: question order and the female-only branch, combined parsing (with the LLM mocked, plus the rule fallback), a forgotten date, a question followed by continuing, Q7 never sent to the LLM, and form edits creating a new version.
  - Notification and Inventory: exact-group matching.
- Backend: exact-group donor candidates, exact-group emergency stock holders, and the rules for the answers endpoints.

**Test data:** fake donors of two blood groups, and one screening done through the chat and then edited with the form.

---

## 4. Migrations

| Phase | Migration | Contents |
|---|---|---|
| 1 | none | — |
| 2 | `SoftDeleteAndAppealReject` | `DeletedAt` columns, filtered doctor unique indexes, `Notifications.DismissedAt`, `Appeals.RejectedAt` |
| 3 | `ActivityLogsAndOversight` | `ActivityLogs`, `AdminSeenMarkers`, admin-suspension columns on requests and transfers |
| 4 | `InventoryAnalysisAndDedupe` | `InventoryAnalysisRuns`, `Notifications.DedupeKey` |
| 5 | none | The agent's SQLite schema is unchanged |

Before each migration, the applied migrations and the affected tables are checked. New columns are nullable and new tables start empty, so the owner's existing data is not changed.

---

## 5. Progress

| Phase | Status |
|---|---|
| 1 | **Done 2026-10-03. Uncommitted; waiting for the owner's review.** |
| 2 | **Done 2026-10-03. Uncommitted; migration applied to the development database (owner-approved).** |
| 3A | **Done 2026-10-03. Uncommitted; migration applied to the development database.** |
| 3B | **Done 2026-10-03 (with change A). Uncommitted; migration applied to the development database.** |
| 4 | **Done 2026-10-04. Uncommitted; migration applied to the development database.** |
| 5 | **Done 2026-10-04. Uncommitted; no database migration (the agent's SQLite schema is unchanged).** |
| Final checks (2026-10-04) | Backend build 0 warnings; frontend lint 98 problems (baseline 104, none in new files); frontend build OK; backend tests 385, 384 pass (only the known `Admin_Dashboard_Statistics_Calculates_All_9_Metrics` fails); agent tests 54/54. After the D11 follow-up (only Critical alerts) and the .gitignore update: backend 386 tests, 385 pass (same known failure); agents 54/54; lint 98; build 0 warnings. Cleanup script `docs/cleanup-test-records.sql` written (not run). My test servers and agents stopped; minted admin sessions ended; my `AdminSeenMarkers` row removed. |

### Phase 1 details

- **Changed files:** `RegisterRequestDto.cs`, `AuthService.cs`, `index.css`, `RegisterPage.jsx`, `RegisterHospitalPage.jsx`, `WaitingForApprovalPage.jsx`, `DonorComplaintsPage.jsx`, `UserProfilePage.jsx`, `lifelink.readme.md`.
- **New file:** `backend.Tests/RegistrationBloodGroupTests.cs`.
- **Migration:** none. `dotnet ef migrations has-pending-model-changes` reports no model changes.
- **Tests:**
  - New: `RegistrationBloodGroupTests`, 10 tests, all pass.
  - Full backend suite: 339 tests; 329 passed, 9 skipped (PostgreSQL-only), and 1 failed, which is the known `Admin_Dashboard_Statistics_Calculates_All_9_Metrics`.
- **Builds:** backend 0 warnings, 0 errors; frontend build OK.
- **Lint:** 104 problems, the same as before. The 5 changed pages have exactly the same 9 problems as at HEAD.
- **UI check:**
  - Register, hospital register (with error state) and hospital waiting page (post-submit and rejected states), in light/dark × desktop/mobile.
  - Profile with the blood group set and with it empty, including the "Add your blood group" dialog.
  - Complaints page with solved, answered, pending and rejected complaints.
  - All API responses were mocked in the browser: no backend was running and nothing was written to the database.
- **Test records created:** none.

### Phase 2 details

- **Migration:** `20261003114646_SoftDeleteAndAppealReject`, applied to the development database on 2026-10-03.
  - Nullable `DeletedAt` on `BloodRequests`, `Complaints`, `Doctors` and `BloodInventories`; `Notifications.DismissedAt`; `Appeals.RejectedAt`.
  - Doctor indexes: `IX_Doctors_Email` is unique on (`Email`) WHERE `"DeletedAt" IS NULL`. `IX_Doctors_HospitalId_LicenseNumber` is unique on (`HospitalId`, `LicenseNumber`) WHERE `"DeletedAt" IS NULL` and replaces `IX_Doctors_LicenseNumber`.
  - Before applying: no duplicate (hospital, SLMC) or doctor email, and no legacy `Deleted` requests.
  - No existing row was changed by this migration.
- **Follow-ups (approved after Phase 2, 2026-10-03):**
  - Data migration `20261003135936_BackfillAppealRejectedAt` fills `RejectedAt` from the first "[REJECTED]" admin message of appeals rejected earlier. It updated 1 row (appeal `76f06616…`). The reject-once rule now uses `RejectedAt` only.
  - Blood groups created (or restored) automatically by adding packets get threshold **5** (was 0) and capacity 100: `InventoryLedger.DefaultMinimumThreshold` / `DefaultMaximumCapacity`. Existing rows are unchanged.
- **Implementation notes:**
  - Assignments closed when a doctor is removed are ignored by every "assigned doctor" lookup.
  - Hospital Verify now creates a new assignment row instead of reusing a closed one.
  - The admin dashboard shows Suspend only for verified (approved) hospitals, because the directory DTO has `isVerified` but no `approvalStatus`. The backend checks `ApprovalStatus`.
- **Tests:**
  - New `SoftDeleteAndGovernanceTests` (8 tests).
  - `BloodRequestDeleteTests` rewritten for soft delete (26 tests).
  - New PostgreSQL test for the filtered doctor indexes.
  - Affected tests updated to the approved rules (lifecycle, hospital donations, complaints, notifications, inventory, appeals, SLMC message, governance seed hospitals marked Approved).
  - Full suite: 353 tests; 352 passed (10 PostgreSQL tests included); 1 failed, which is the known `Admin_Dashboard_Statistics_Calculates_All_9_Metrics`.
- **Builds:** backend 0 warnings, 0 errors; frontend build OK.
- **Lint:** 104 problems, unchanged; every changed file has the same count as at HEAD.
- **End-to-end on the development database** (test backend on port 5232, email and agents off): 28/28 checks passed.
- **UI check:**
  - With fake accounts: My Requests delete dialog; the donor's closed acceptance; the deleted-request message; the doctor delete dialog.
  - With a mocked admin API: hospitals list (Suspend vs "Awaiting registration approval"); doctors list ("Removed"); deleted complaint (read-only); appeal ("Send another message", "Already Rejected").
  - Light/dark and desktop/mobile.
- **Test records created** (fake, `@example.test`, password `P2test@1234`):
  - Hospital "P2 Test Hospital" `e875ef0b-f47b-4d47-a20e-e425ed639884` (approved directly in the DB) and its staff login `00315dee-970b-4385-901a-160873afe947`.
  - Doctor `7c497b08-c678-4da4-acb6-4c27093ab08c`, now removed; its login `614d5098-cc2b-4549-a6c3-618682009d8d` was retired.
  - Doctor `26ddc93e-4181-4606-9fd5-c82ecd79b827` (same email and SLMC, re-added); login `3ec58970-a087-499b-969d-6c51ce3c0fd5`.
  - Patient `909f2816-017b-4a36-8c47-55206fd8d67b` and donor `54e77877-5d27-4987-bea8-465aca367845` (AB-).
  - Requests `2a9aefb6-1dff-4b44-a262-0109a3bc7962` (Deleted) and `02843c3b-e76c-4287-b9b7-759d25664471` (Pending).
  - Verification `c4257fc4-646a-4639-92db-5d38865785ec`; acceptance `11c16c42-a240-4ff7-ad24-94be5e8b7d5a` (closed by the delete); complaint `b3325825-ba29-404e-aaae-a96d22699035` (deleted).
  - 5 notifications: 1 AB- donor alert, 3 "Blood Request Deleted" (hospital, doctor, donor; the donor's one dismissed), and 1 in-app "Complaint Deleted" to the Admin account (the only row addressed to an existing account).
  - 7 `UserSessions` rows for these logins.
- **Files changed in Phase 2:**
  - Backend:
    - Entities: `Appeal.cs`, `BloodInventory.cs`, `BloodRequest.cs`, `Complaint.cs`, `Doctor.cs`, `Notification.cs`.
    - Data: `AppDbcontext.cs`.
    - Migrations: `20261003114646_SoftDeleteAndAppealReject.cs`, its `.Designer.cs`, `AppDbContextModelSnapshot.cs`.
    - DTOs: `AcceptanceResponseDto.cs`, `AppealResponseDto.cs`, `ComplaintResponseDto.cs`, `DoctorResponseDto.cs`.
    - Controllers: `BloodRequestsController.cs`, `DoctorsController.cs`, `NotificationsController.cs`, `ProfilesController.cs`, `SearchController.cs`.
    - Services: `AcceptanceService.cs`, `AdminService.cs`, `AppealService.cs`, `AssistantContextBuilder.cs`, `BloodRequestService.cs`, `DoctorAssignmentRules.cs`, `EmailUniquenessHelper.cs`, `SlmcUniquenessHelper.cs`, `ComplaintService.cs`, `DoctorService.cs`, `HospitalActivityService.cs`, `BloodInventoryService.cs`, `InventoryLedger.cs`, `InventoryMonitor.cs`, `NotificationAgentService.cs`, `NotificationFactory.cs`, `VerificationService.cs`.
  - Tests:
    - New: `SoftDeleteAndGovernanceTests.cs`.
    - Changed: `BloodInventoryServiceTests.cs`, `BloodRequestDeleteTests.cs`, `BloodRequestLifecycleTests.cs`, `ComplaintWorkflowTests.cs`, `GovernanceRedesignTests.cs`, `HospitalPacketsAndDonationsTests.cs`, `PostgresRaceTests.cs`, `ProfileSlmcTests.cs`.
  - Frontend:
    - `NotificationCenterDrawer.jsx`
    - Admin pages: `AdminAppealsPage.jsx`, `AdminComplaintsPage.jsx`, `AdminDashboard.jsx`.
    - Doctor pages: `ScreeningReportsPage.jsx`.
    - Donor pages: `MyAcceptancesPage.jsx`, `MyRequestsPage.jsx`, `RequestDetailPage.jsx`, and `DonorComplaintsPage.jsx` (also changed in Phase 1).
    - Hospital pages: `DoctorManagementPage.jsx`, `DonateBloodPage.jsx`.
  - Docs: `lifelink.readme.md` and `docs/plan-phases-1-5.md` (both also changed in Phase 1).

### Phase 3A details

- **Migration:** `20261003142423_ActivityLogsAndAdminSeen`, applied to the development database. It adds two tables and changes nothing existing:
  - `ActivityLogs` (append-only, no foreign keys; indexes on (ActorUserId, OccurredAt), (HospitalId, OccurredAt) and (SubjectUserId, OccurredAt)).
  - `AdminSeenMarkers` (PK AdminUserId + Area).
- **Endpoints:**
  - `GET /api/activity-logs/my`
  - `GET /api/Admin/users/{id}/activity-log`
  - `GET /api/Admin/hospitals/{id}/activity-log`
  - `GET /api/Admin/blood-requests`
  - `GET /api/Admin/attention-counts`
  - `PUT /api/Admin/attention/{area}/seen`

  All transfers for the Admin come from the existing `GET /api/transfers`.
- **Recorded actions:** as listed in `lifelink.readme.md` §9.10, written in the same save as each action. No sign-in or sign-out.
- **UI:**
  - The admin view of user and hospital profiles has an "activity log" section.
  - `/admin/activity` has Blood requests and Transfers tabs, a "New" badge, status/type/date filters and pagination.
  - The sidebar has an "Activity Log" item and count badges on Activity Log, Registrations, Complaints and Appeals; the dashboard hub cards show the pending counts.
  - The "my activity" endpoint and component are ready for the dashboards in 5.3/5.4.
- **Tests:**
  - New `ActivityLogAndAttentionTests` (8 tests).
  - Full suite: 362 tests; 361 passed (10 PostgreSQL tests included); 1 failed, the known `Admin_Dashboard_Statistics_Calculates_All_9_Metrics`.
  - End to end on the development database: 26/26 checks passed.
- **Builds:** backend 0 warnings, 0 errors; frontend OK. **Lint:** 104, unchanged; the new files have 0 problems.
- **UI check:**
  - With real data and a minted dev admin token: `/admin/activity` (light desktop, dark mobile), the user log (light desktop, filtered on light mobile), the hospital log (dark desktop).
  - Sidebar and dashboard badges (light desktop, dark mobile drawer) used a mocked badge-count response only.
- **Test records created** (fake "P2 Test" accounts from Phase 2):
  - Request `925298ce-a468-4e5a-acef-a09bc6174e74` (created, edited, cancelled).
  - Request `e69282a8-5c6a-4727-a133-969fa4fb52b4` (Pending).
  - Packets PKT-00000035 and PKT-00000036 (B-) and blood group row `ca3bec35-0ab9-490c-a393-b6bf9ba6f7bb` (B-, auto-created with threshold 5, then set to 3 / 60) at P2 Test Hospital.
  - The doctor `p2.doctor@example.test` changed the temporary password to `P2test@5678`.
  - The donor `p2.donor@example.test` was suspended and reinstated by the admin (in-app notifications to that donor; no email sent).
  - 9 activity log rows from the end-to-end run, plus 1 from the second request.
  - Session rows for the fake logins.
  - A minted admin session `01097d9a-4492-4367-9735-b25d74f3e7dc`, ended afterwards. The `AdminSeenMarkers` row my UI visits created was removed, so the admin's Activity log starts as "never opened".
- **Files changed in 3A:**
  - Backend:
    - New: `Entities/ActivityLog.cs`, `DTOs/ActivityLogs/ActivityLogDtos.cs`, `DTOs/Admin/AdminAttentionDto.cs`, `Services/Common/ActivityLogger.cs`, `Services/Common/ActivityLogQueries.cs`, `Controllers/ActivityLogsController.cs`, `Controllers/AdminOversightController.cs`, migration `20261003142423_ActivityLogsAndAdminSeen` (+ Designer).
    - Changed: `Data/AppDbcontext.cs`, `Migrations/AppDbContextModelSnapshot.cs`, `Controllers/AdminController.cs`, `Controllers/ProfilesController.cs`, `DTOs/BloodRequests/BloodRequestResponseDto.cs`, `Services/Admin/AdminService.cs`, `Services/Admin/IAdminService.cs`, `Services/BloodRequests/BloodRequestService.cs`, `Services/BloodRequests/IBloodRequestService.cs`, `Services/Acceptances/AcceptanceService.cs`, `Services/Appeals/AppealService.cs`, `Services/Auth/AuthService.cs`, `Services/Complaints/ComplaintService.cs`, `Services/Doctors/DoctorService.cs`, `Services/Emergency/EmergencyRequestService.cs`, `Services/Hospitals/HospitalService.cs`, `Services/Inventory/BloodInventoryService.cs`, `Services/Transfer/TransferRequestService.cs`, `Services/Verification/VerificationService.cs`.
  - Tests: new `backend.Tests/ActivityLogAndAttentionTests.cs`.
  - Frontend:
    - New: `api/activityApi.js`, `context/useAdminAttention.js`, `components/activity/ActivityLogList.jsx`, `pages/admin/AdminActivityPage.jsx`.
    - Changed: `api/index.js`, `App.jsx`, `components/common/Sidebar.jsx`, `pages/admin/AdminDashboard.jsx`, `pages/profiles/UserProfilePage.jsx`, `pages/profiles/HospitalProfilePage.jsx`.
  - Docs: `lifelink.readme.md`, `docs/plan-phases-1-5.md`.
- **Follow-ups done before 3A (Phase 2 follow-ups):** `Migrations/20261003135936_BackfillAppealRejectedAt.cs` (+ Designer), `Services/Inventory/InventoryLedger.cs`, `Services/Appeals/AppealService.cs` (RejectedAt only), `backend.Tests/BloodInventoryServiceTests.cs`.

### Phase 3B details (2.2 messages, 2.3 suspend/lift, change A)

- **Migration:** `20261003154916_AdminSuspensionOfRequestsAndTransfers`, applied to the development database. It adds six nullable columns and changes no existing data:
  - `BloodRequests`: `AdminSuspendedAt`, `AdminSuspendedByUserId`, `AdminSuspensionReason` (varchar 500).
  - `HospitalTransferRequests`: the same three columns.
- **Endpoints** (Admin only, in `AdminOversightController`):
  - `PUT /api/Admin/blood-requests/{id}/suspend` `{reason}` and `PUT .../lift`.
  - `PUT /api/Admin/transfers/{id}/suspend` `{reason}` and `PUT .../lift`.
  - `POST /api/Admin/messages` `{userId | hospitalId, subject, message}`.
- **Rules:**
  - **What can be suspended:** open requests (Pending, Verified or Approved) and pending transfers. A reason is required (max 500 characters).
  - **Who is notified:** on suspend and on lift, the creator (or nobody extra when a hospital or the admin created it), the request's hospital, the assigned doctor, and the active donors / donating hospitals. Donors get a "you can still withdraw" text. Transfers notify both hospitals. Every suspend and lift is logged (for a transfer, in both hospitals' logs).
  - **Guard (`SuspensionGuard`, 409 "temporarily suspended by the administrator"):** blocks every action:
    - Requests: edit, cancel.
    - Hospital: verify, reject.
    - Doctor: approve or reject a request, approve or reject a screening report, decide a hospital donation.
    - Donor: accept, finalize, release, "update my answers" (reopen).
    - Hospital donation offers.
    - Agent callbacks: screening opened, report submitted.
    - Transfers: accept, reject, withdraw.
  - **Allowed while suspended (Q7):**
    - A donor (or a hospital donating) withdraws.
    - The creator deletes the request. Every admin then gets an in-app "Suspended Blood Request Deleted" notification, the log entry says it was suspended, and the admin list shows both Deleted and "Suspended by admin".
  - **Hidden and skipped:** suspended requests are hidden from the public list and skipped by the expiry sweep (and the on-view expiry). The first sweep after the lift expires an overdue request.
  - **Screening agent:** reads `requestSuspended` from the acceptance and answers "screening is paused" (status `Paused`). The answers are kept.
  - **Messages:**
    - The notification type is `AdminMessage`, titled "Message from Administrator: <subject>".
    - Doctors, admins and hospital staff accounts are refused as user recipients (use the hospital profile).
    - Exactly one recipient; subject 3–120 and message 5–2000 characters.
    - Logged as `Admin.MessageSent` in the recipient's log. A suspended account receives it and can read it once reinstated.
  - **Change A:** the backend already accepted a complaint without a target. The work was wording and display:
    - The form says "Target (optional)" with "Leave this empty to ask the administrator a general question or raise a doubt".
    - The admin page shows a "General question (no target)" badge and "Target Facility: None (general question to the admin)".
    - Logic that needs a target (see final report) treats such complaints sensibly.
- **UI:**
  - `/admin/activity`: Action column (Suspend with a reason dialog / Lift), "Suspended by admin" badge and status filter.
  - "Message from Administrator" card and dialog on the admin view of user (donor/patient only) and hospital profiles.
  - "Suspended by admin" badge with actions hidden on:
    - My Requests (Delete stays).
    - Verify Requests.
    - Doctor dashboard.
    - My Acceptances (only Withdraw stays).
    - Transfers.
- **Tests:**
  - New `AdminSuspensionAndMessagesTests` (9 tests, all pass): reason/open-only, notifications + log, every guarded action returns 409, Q7 withdraw + delete + admin notification, public list + expiry sweep, suspend racing an edit (concurrency), transfers, message rules, complaint without a target.
  - End to end on the development database: 23/23 checks passed (2 after fixing my script's routes).
- **UI check:** light/dark × desktop/mobile on `/admin/activity` (plus the suspend dialog), the user profile (plus the message dialog), My Requests, Transfers and Complaints, using fake accounts and a minted admin token.
- **Test records created:**
  - Fake hospital "P3B Test Hospital B" `47842c56-6153-4914-be27-a7d1ed179822` (staff login `p3b.hospital@example.test`, user `5921e723-6ab6-429a-977a-f3294d904e3a`, password `P2test@1234`), approved directly in the database.
  - Transfers `c82006fb-c007-41ed-91cc-4d764e118555` and `50c07f13-a399-4cc7-aaa3-defef8c0fe2f` (B -> P2 hospital, suspended, lifted, withdrawn = Cancelled).
  - Request `e69282a8…` (3A record) suspended and lifted twice, edited once (B-, 1 unit).
  - Complaint `daa33193-fbe4-4592-a8c4-4b13608c246a` (general question by `p2.donor`).
  - Two admin messages (to `p2.donor` and P2 Test Hospital).
  - Suspend/lift notifications and activity log rows.
  - Minted admin sessions `a336064c-80b1-4017-a8dd-c22f981d0f0d` and `ae27947a-c8be-4c3d-8fe2-9459bd86faa0` (ended at the end of the run).
  - Session rows for the fake logins.
- **Files changed in 3B:**
  - Backend:
    - New: `Services/Common/SuspensionGuard.cs`, `Services/Admin/AdminOversightActionsService.cs`, `DTOs/Admin/AdminMessageDto.cs`, migration `20261003154916_AdminSuspensionOfRequestsAndTransfers` (+ Designer).
    - Changed: `Entities/BloodRequest.cs`, `Entities/HospitalTransferRequest.cs`, `Data/AppDbcontext.cs`, `Migrations/AppDbContextModelSnapshot.cs`, `Program.cs`, `Controllers/AdminOversightController.cs`, `DTOs/BloodRequests/BloodRequestResponseDto.cs`, `DTOs/Transfer/TransferRequestResponseDto.cs`, `DTOs/Acceptances/AcceptanceResponseDto.cs`, `Services/BloodRequests/BloodRequestService.cs`, `Services/BloodRequests/RequestExpiryService.cs`, `Services/Acceptances/AcceptanceService.cs`, `Services/Verification/VerificationService.cs`, `Services/Transfer/TransferRequestService.cs`.
  - Agent: `Agents/RequestManagement/services/screening_service.py`, `api/routes.py` (paused state).
  - Tests: new `backend.Tests/AdminSuspensionAndMessagesTests.cs`.
  - Frontend:
    - New: `components/admin/AdminMessageButton.jsx`.
    - Changed: `api/activityApi.js`, `components/common/Badge.jsx` (`SuspendedBadge`), `pages/admin/AdminActivityPage.jsx`, `pages/profiles/UserProfilePage.jsx`, `pages/profiles/HospitalProfilePage.jsx`, `pages/donor/MyRequestsPage.jsx`, `pages/hospital/VerifyBloodRequestsPage.jsx`, `pages/doctor/DoctorDashboard.jsx`, `pages/donor/MyAcceptancesPage.jsx`, `pages/hospital/TransfersPage.jsx`, `pages/donor/DonorComplaintsPage.jsx`, `pages/admin/AdminComplaintsPage.jsx`.

### Phase 4 details (5.1, 5.2, 5.3, 5.4, 5.5)

- **Migration:** `20261003162305_InventoryAnalysisAndDedupe`, applied to the development database. Nothing existing changes:
  - `Notifications.DedupeKey` (nullable varchar 200) and an index (HospitalId, DedupeKey).
  - New table `InventoryAnalysisRuns` (RunId, StartedAt, FinishedAt, Trigger, TriggeredByHospitalId, TriggeredByUserId, Status, LowStockAlerts, ExpiringAlerts, SkippedDuplicates), with indexes on StartedAt and (Trigger, StartedAt).
- **5.1 Doctors pending first login:**
  - `DoctorAssignmentRules` refuses `MustChangePassword` doctors: "This doctor hasn't signed in and changed the temporary password yet...". This covers creating a hospital request and Verify.
  - A pending doctor is never a fallback decider. They are left out of the fallback list, refused for hospital-donation decisions and refused for screening-report review.
  - Both pickers hide pending doctors and explain why. Doctor Management shows "Pending first login".
  - **Fallback effect:** the fallback rule (assigned doctor removed or inactive → any doctor of the hospital) now means any doctor of the hospital *who has completed first login*. If none has, the item waits, and the doctor sees why after the first sign-in.
- **One "below threshold" rule:** `UnitsAvailable < MinimumThreshold` (`Services/Inventory/InventoryRules.cs`). It is used by:
  - The low-stock endpoint and the `InventoryResponseDto.IsLowStock` flag.
  - The dashboard and the inventory page, which read the flag.
  - The rule-based alerts and the Inventory agent (`is_below_threshold`).

  Before this change the backend list used `<=` and the dashboard `<= (threshold || 5)`. A group exactly at its threshold is no longer "low".
- **Inventory agent: before and after:**

  | | Before | Now |
  |---|---|---|
  | Who is alerted when a group is low | Only the low hospital | The low hospital **and every other hospital holding that exact group above its own threshold** (`InventoryShortageHelp`) |
  | What the low hospital is told | "below your threshold of N", plus hospitals with "spare" stock (`current - threshold`) | "You have only 2 units of A+ left. Hospitals holding A+: B (12 units), ..." (unit counts only) |
  | Expiring packets matched to public requests | Compatible groups | Exact group (Q3) |
  | Repeat check | Unread alert with the same type + title within 12 h; AI-rewritten titles defeated it | `DedupeKey` (type + group [+ low hospital]) within 12 h, passed through the Supervisor and Notification agent unchanged; the title rule stays for alerts without a key |
  | Surplus alerts | none | none (the old LangGraph surplus workflow in the Inventory agent is not called by LifeLink and was left unchanged) |
- **5.5 Manual analysis:**
  - `POST /api/Inventory/analysis/run` and `GET /api/Inventory/analysis/status` (hospital staff only).
  - The lock is the lease row `InventoryAnalysis`: atomic statements, a 5-minute safety expiry while running, and a 2-minute global cooldown after each run.
  - The scheduler uses the same lock and reads its last scheduled run from `InventoryAnalysisRuns` (Q21).
  - A failed run is recorded as Failed (503 to the caller). A run left Running past 5 minutes shows as "Interrupted".
  - Manual runs are logged in the hospital's activity log.
- **UI:**
  - **Inventory page:** one "Blood groups" card with "Add blood group", "Add packets", search (group or tracking number) and a stock filter (below threshold / expiring soon) in the header. Group rows expand to their packets (tracking no., collected, expiry, created by, status, Edit / Issue / History) with a status filter inside, and paging of 10. `?group=` expands and scrolls to a group. The separate packets card is gone; thresholds stay editable.
  - **Hospital dashboard:**
    - Unit cards link to `/hospital/inventory?group=...`.
    - The Critical emergencies and Low stock cards open detail dialogs.
    - The analysis panel replaces the static card with the made-up numbers. It shows: last run in Sri Lanka time, scheduled or manual by which hospital, and the result; the next scheduled run, "due now" or "Scheduled runs are off"; "Analysis running..."; "Available again in m:ss"; a 15-second background refresh; and a result toast.
    - The hospital's activity log is at the end.
  - **Donor/patient dashboard:** "My activity" at the end.
- **Tests:**
  - New `InventoryAnalysisAndDoctorRulesTests` (8 tests, all pass) and a new PostgreSQL test `The_Inventory_Analysis_Lock_Admits_One_Run_Then_A_Cooldown_Then_Frees_Itself` (passes, uses a test-only lease name, rolled back).
  - Inventory agent tests rewritten (5 pass), Notification agent tests (4 pass), Supervisor planner test added (9 pass).
  - End to end on the development database: 17/17.
  - Before running the analysis I checked that no real hospital was low or had expiring packets, so only the fake hospitals were alerted.
- **UI check:** light/dark × desktop/mobile on the inventory page (with `?group=B-`), the hospital dashboard, Doctor Management and the donor dashboard. All seven analysis panel states use a mocked status response. The low-stock and emergency dialogs are covered too.
- **Test records created:**
  - Fake doctor "P4 Test Pending" `544abf5a-976d-41f3-a920-d9488b7d68bc` (login `p4.doctor@example.test`, user `53e36d6b-228b-4d12-ae23-85ffd9f80386`), still pending first login.
  - Packets PKT-00000037 to PKT-00000042 (B-) and the auto-created B- group (threshold 5) at P3B Test Hospital B.
  - Analysis runs `bf4b7f1f-7a49-4ddf-9637-0cc6290e390b` and `08e74c82-5fc5-4ebd-aebc-7d0771ed2e21`, plus the lease row `InventoryAnalysis` (created by my first run, left free).
  - Two inventory alerts (to the two fake hospitals), activity log rows, session rows, and a minted admin session (listed in the final report).
- **Files changed in Phase 4:**
  - Backend:
    - New: `Entities/InventoryAnalysisRun.cs`, `Services/Inventory/InventoryAnalysisService.cs`, `Services/Inventory/InventoryRules.cs`, migration `20261003162305_InventoryAnalysisAndDedupe` (+ Designer).
    - Changed: `Entities/Notification.cs`, `Data/AppDbcontext.cs`, `Migrations/AppDbContextModelSnapshot.cs`, `Program.cs`, `Controllers/InventoryController.cs`, `DTOs/Inventory/InventoryResponseDto.cs`, `DTOs/Planning/PlanResponseDto.cs`, `Services/Inventory/InventoryMonitor.cs`, `Services/Inventory/BloodInventoryService.cs`, `Services/Notification/INotificationAgentService.cs`, `Services/Notification/NotificationAgentService.cs`, `Services/BloodRequests/RequestExpiryBackgroundService.cs`, `Services/BloodRequests/BloodRequestService.cs`, `Services/Acceptances/AcceptanceService.cs`, `Services/Verification/VerificationService.cs`, `Services/Common/DoctorAssignmentRules.cs`.
  - Agents: `InventoryManagement/services/analysis_service.py`, `InventoryManagement/tests/test_analysis.py`, `Supervisor/services/planner.py`, `Supervisor/graph/nodes.py`, `Supervisor/tests/test_events.py`, `Notification/app.py`, `Notification/models.py`, `Notification/prompts.py`.
  - Tests: new `backend.Tests/InventoryAnalysisAndDoctorRulesTests.cs`; `backend.Tests/PostgresRaceTests.cs` (one test added).
  - Frontend: `api/inventoryApi.js`, `components/workflow/InventoryAnalysisStatus.jsx` (rewritten), `pages/hospital/InventoryManagementPage.jsx`, `pages/hospital/HospitalDashboard.jsx` (rewritten), `pages/donor/DonorDashboard.jsx`, `pages/hospital/DoctorManagementPage.jsx`, `pages/hospital/VerifyBloodRequestsPage.jsx`, `pages/donor/CreatePatientRequestPage.jsx`.

### Phase 5 details (4.4, 6.1, 6.2, 7.1–7.8)

- **Blood request alerts (owner's decision):**
  - **Before:**
    - For every approved request, Normal included, all eligible donors whose saved group was **compatible** with the request were alerted (`EligibleDonorAlert`), through the Supervisor, the Notification agent or the backend fallback.
    - For High/Critical requests, every approved non-suspended hospital was also alerted (`UrgentHospitalAlert`), whatever stock it held.
  - **Now:**
    - **Normal requests alert nobody.** Donors find them in the public list; the creator, the hospital and the assigned doctor still get their usual status notifications.
    - **Normal and High ("Urgent") requests alert nobody** (D11, owner's follow-up). **Critical requests only** alert eligible donors whose saved group is **exactly** the request's (users without a saved group never), and the hospitals holding unexpired Available packets of that exact group.
  - Code: `VerificationService.DispatchApprovalAlertsAsync`, `NotificationAgentService` (`IsAlertPriority`, `GetEligibleDonorCandidatesAsync`, `GetAlertHospitalIdsAsync(…, bloodGroup)`, fallback), `Agents/Notification/nodes.py` (`alert_groups`, `URGENT_PRIORITIES`), `prompts.py`.
- **Emergency Center (6.1):** `FindHospitalsWithExactGroupStockAsync`, exact group only; the wording is "You hold N unit(s) of B-" (backend fallback, Notification template, Supervisor summary, Emergency Center page).
- **Every compatible-group place and what happened to it:**

  | Place | Now |
  |---|---|
  | `NotificationAgentService.BloodCompatibility` (donor alerts) | **Removed**: exact group |
  | `Notification/nodes.py` `COMPATIBILITY_MATRIX` and the ranking prompt | **Exact group** (`alert_groups`) |
  | `EmergencyRequestService.CompatibleDonorGroups` | **Removed**: exact group |
  | `InventoryManagement/services/analysis_service.py` `COMPATIBLE_DONORS` (expiring packets) | **Removed in Phase 4**: exact group |
  | `Notification/app.py`, `Supervisor/services/planner.py`, `EmergencyHubPage.jsx` | Wording only |
  | `BloodCompatibilityService` (accepting with a compatible group, tested-group check) | **Unchanged** (Q14) |
  | Supervisor `intents.py`, medical knowledge docs | Unchanged; the platform docs (`blood-requests.md`, `inventory-and-transfers.md`) and the Supervisor README were updated |
- **Screening (7.1–7.8):**
  - **Which code runs it:** the frontend `ScreeningInterviewPage` → backend `POST /api/assistant/chat` → Supervisor (`request_management_node`) → Request Management agent (`Agents/RequestManagement`, port 8001, SQLite `screening_agent.db`, still the live store; no schema change).
  - **The questions:** exactly the owner's **7 questions** (`models/donor_screening.py`), each with typed parts, plus the final "I confirm my answers are true" tick, which is required and not counted. Occupation, NIC, address, email, emergency contact and the 12 sections are gone.
  - **Inputs inside the chat bubble (7.8):** options, tick boxes with "None of these", dates with "I don't remember", numbers, and a country + return-date list. Free text still works:
    - Rules read confirmations, a single missing part, yes/no runs and "none".
    - Otherwise Gemini extracts the fields, validated by the same rules (never for question 7).
    - Otherwise the agent asks one part at a time.
  - **Follow-ups:** a ticked tattoo, surgery or transfusion, "yes" to travel, or a missing part gets a short follow-up for the same question (not counted). The pregnancy part is shown only to female donors. No help texts or tooltips.
  - **7.5:** "I don't remember" is stored as "Donor doesn't remember", gives an information flag, and is never asked again.
  - **7.6:** a donor's question is answered from the knowledge base, or else by a guarded Gemini explanation in simple words labelled "General information, not from LifeLink's knowledge base; ask the reviewing doctor" (`Supervisor/graph/nodes.py` `answer_screening_question`). The question is then repeated. Question 7 uses a fixed topic only.
  - **Rules (`services/eligibility_rules.py`):** only flags, never a decision:
    - Likely deferral (`defer`): age outside 18–60; first-time donor over 55; weight 50 kg or less; last donation under 120 days ago; tattoo under 2 years ago; malaria-risk country within 3 years or any other country within 3 months; pregnant, breastfeeding or gave birth in the last year; risk behaviour "yes".
    - Review: illness, medicines, surgery, transfusion, unwell, infection, no meal, under 6 h sleep, alcohol in 24 h, unknown travel or tattoo date.
    - Information: unknown last donation date.
    - Haemoglobin above 12.5 is noted for the doctor's check.
    - At the end the donor is reminded to bring the NIC.
  - **Report:** schema `lifelink.screening.v2`. The doctor's view is unchanged (7.1): risk level, recommendation, summary, flags and 7 sections, with question 7 confidential. Extra keys for the donor: `questionnaire` and `form_answers`.
  - **Old sessions:** sessions from the 12-section questionnaire restart with the 7 questions when the donor next opens them. Old reports stay as they were.
  - **Existing rules that needed answers no longer collected:** removed or replaced as listed in section B of the review: age yes/no → date of birth; weight yes/no → number; 120-day yes/no → date; current symptoms → 2-week infection; treatment → medicines; 24 conditions → 7; 10 events / 12 months → 3 events / 2 years; diseases, hepatitis contact, anti-malarials, dental, recent symptoms, abortion and miscarriage removed; malaria yes/no → country list; infection-risk checklist → yes/no.
  - **7.2 Edit form and 7.3 donor view:**
    - `GET /api/Acceptances/{id}/screening-answers` (own acceptance only; the answers without the AI fields, `canEdit`).
    - `PUT /api/Acceptances/{id}/screening-answers`: the agent checks the full set first (`POST /api/agent/screening/answers/{id}/validate`; nothing changes if invalid or unreachable). Then the waiting version is superseded, and the agent re-checks and submits the new version in the background (`PUT /api/agent/screening/answers/{id}`, 202). No chat.
    - Frontend: "View my answers" and "Update my answers" open `ScreeningAnswersForm` (the same inputs as the chat). Reports from the old questionnaire still use the chat to update.
  - **Paused while suspended (3B):** the chat shows "Screening is paused"; editing is refused.
- **Knowledge base:** `Agents/Supervisor/.chroma` does not exist. The Supervisor still answers from the same documents by keyword search. Building the vector store (`cd Agents/Supervisor && .venv\Scripts\python -m knowledge.ingest`) needs a Gemini key in `Agents/Supervisor/.env`. The only key on this machine is in `Agents/Notification/.env`, so I did not build it (that would mean copying the secret).
- **Tests:**
  - Request Management pytest rewritten: 19 tests.
  - Notification: 4. Supervisor: 26 (2 assertions updated for the new wording/signature, 1 test added). Inventory: 5.
  - Backend:
    - New `ScreeningAnswersTests` (4) and `EmergencyRequestServiceTests.Emergency_Alerts_Go_Only_To_Hospitals_Holding_The_Exact_Blood_Group`.
    - `Student2WorkflowsTests` two alert tests rewritten for the new rules.
    - `AgenticWorkflowRulesTests` candidate test updated to exact group.
    - Fixture doctors marked "first login completed" (`MustChangePassword = false`) in 9 test files, because the 5.1 rule now refuses pending doctors.
  - End to end: alerts 7/7 (no real account notified); screening through my own Supervisor (8104) and Request Management agent (8101, scratch SQLite DB, no Gemini key), 15/15.
- **UI check:**
  - Screening chat with inputs inside the bubble (light/dark × desktop/mobile, plus question 6 with tick boxes, "I don't remember" and trips).
  - My Acceptances (4).
  - Edit form (light desktop, dark mobile) and view (dark desktop, light mobile).
  - Emergency Center wording (4).
- **Test records created:**
  - Fake donor `p5.donor@example.test` `371949f6-d643-4703-8818-fcb4685027f5` (B-, Female).
  - Requests `a6375cef-4bc8-4ee0-8f67-675c21b5d912` (Critical B-, by `p2.donor`, approved; suspended and lifted once) and `45f4a6a0-3ecd-475f-9229-6999cdbc5892` (Normal AB+, approved).
  - Emergency `563d2285-107d-4e90-bc84-9fa0dc3c907e`.
  - Acceptance `0521380e-45a0-4c16-b7be-4193dc288ea0` (p5 donor, report v1 Superseded, v2 Pending).
  - Acceptance `67111635-83ae-46ee-a527-c9028ab59721` (`p2.patient` on the AB+ request, screening in progress).
  - Alerts and notifications to the fake accounts, activity log rows, and session rows.
  - The agent records are only in the scratch SQLite DB `screening_test.db` (scratchpad), not in `screening_agent.db`.
- **Files changed in Phase 5:**
  - Backend:
    - New: `Services/Acceptances/ScreeningAgentClient.cs`, `DTOs/Acceptances/ScreeningAnswersDto.cs`.
    - Changed: `Services/Notification/NotificationAgentService.cs`, `INotificationAgentService.cs`, `Services/Verification/VerificationService.cs`, `Services/Emergency/EmergencyRequestService.cs`, `Controllers/EmergencyRequestsController.cs`, `Services/Acceptances/AcceptanceService.cs`, `IAcceptanceService.cs`, `Controllers/AcceptancesController.cs`, `DTOs/Assistant/AssistantDtos.cs`, `Services/Assistant/AssistantService.cs`, `Program.cs`.
  - Agents:
    - Request Management: `models/donor_screening.py`, `models/api_models.py`, `services/answer_parser.py`, `services/eligibility_rules.py`, `services/report_service.py`, `services/screening_service.py`, `services/gemini_service.py`, `config/prompts.py`, `graph/workflow.py`, `graph/state.py`, `api/routes.py`, both test files.
    - Supervisor: `graph/nodes.py`, `models/request.py`, `services/agent_clients.py`, `services/planner.py`, `knowledge/platform/blood-requests.md`, `knowledge/platform/inventory-and-transfers.md`, `README.md`, `tests/test_chat.py`.
    - Notification: `nodes.py`, `prompts.py`, `app.py`, `tests/test_notification_agent.py`.
  - Tests: new `backend.Tests/ScreeningAnswersTests.cs`; changed `EmergencyRequestServiceTests.cs`, `Student2WorkflowsTests.cs`, `AgenticWorkflowRulesTests.cs` and the fixture files above.
  - Frontend:
    - New: `components/screening/ScreeningParts.jsx`, `components/screening/screeningValues.js`, `components/screening/ScreeningAnswersForm.jsx`.
    - Changed: `pages/donor/ScreeningInterviewPage.jsx` (rewritten), `pages/donor/MyAcceptancesPage.jsx`, `components/assistant/ChatThread.jsx`, `api/assistantApi.js`, `api/acceptanceApi.js`, `pages/hospital/EmergencyHubPage.jsx`.

## 6. Decisions to review

Decisions I made myself during 3B, 4 and 5 (the option that changes least), for the owner to confirm or change.

| # | Part | Decision | Why |
|---|---|---|---|
| D1 | 3B | The suspension guard only checks the flag; it does not mark the request as changed. A suspend therefore beats a racing action only when that action also changes the request row (edit, cancel, verify, approve, accept, finalize, delete). A report submission, which adds a row without changing the request, can still slip in at the exact same moment. | Making every action touch the request row would turn two donors acting at once on the same request into 409 conflicts for each other, which changes existing behaviour. |
| D2 | 3B | The suspension reason is shown to everyone notified and on the "Suspended by admin" badge tooltip. | The admin dialog says so; it avoids a separate "admin-only note" field. |
| D3 | 3B | A hospital donating blood (hospital donation offer) may withdraw while suspended, like a donor. | Owner's Q7 "a donor can withdraw"; a hospital donating is a donor. |
| D4 | 3B | Hospital staff accounts are refused as user recipients of a message: the message goes through the hospital's profile instead. | Hospital messages are hospital notifications that every staff session sees. |
| D5 | 3B | Lifting the suspension of a request that was deleted while suspended sends no notifications (it is only logged). | Nobody can act on a deleted request. |
| D6 | 4 | The planner's cap of 3 alerts per hospital per run stays. The order is: the hospital's own shortage, then help requests, then expiring packets. A hospital holding several groups that are low elsewhere could miss a 4th help alert in that run (it comes in a later run). | Least change to the existing planner; with 8 blood groups this is rare. |
| D7 | 4 | The low hospital's alert lists at most 5 holder hospitals (largest stock first); every holder still gets its own help alert. | Keeps the alert short. |
| D8 | 4 | The "Surplus" badge on the inventory table stays (it is a display badge, not an alert). | "No surplus alerts" is about notifications. |
| D9 | 4 | On the test backend scheduled runs were off (`InventoryMonitoring:Enabled=false`), so the panel's scheduled states were checked with a mocked status. | The scheduler would otherwise alert real hospitals. |
| D10 | 4 | A doctor pending first login who was assigned before this change (old data) keeps the assignment; only new assignments are refused. | Changing existing assignments would change real records. |
| D11 | 5 | **Decided by the owner (follow-up, 2026-10-04): only Critical requests send alerts.** High ("Urgent") behaves like Normal: no alerts to donors or hospitals; the creator, hospital and assigned doctor still get their usual status notifications. Changed in `NotificationAgentService.IsAlertPriority`, `VerificationService.DispatchApprovalAlertsAsync` (comment), `Agents/Notification/nodes.py` `URGENT_PRIORITIES`, the platform knowledge doc and the READMEs. | Owner's decision. |
| D12 | 5 | The edit form talks to the Request Management agent directly from the backend (`ScreeningAgent:BaseUrl`, default `http://127.0.0.1:8001`), not through a Supervisor event. | Validation must happen before the old version is superseded (decided report versions cannot be restored); a direct call keeps the Supervisor unchanged. |
| D13 | 5 | Malaria-risk countries: a fixed list in `eligibility_rules.py` from the WHO World Malaria Report 2023 (countries with indigenous cases in 2022), without countries WHO has since certified malaria-free (Sri Lanka, Belize, Cabo Verde). Unknown country names are treated as "other travel" (3-month rule). | The owner asked for a source comment; the NBTS deferral list should be checked against it. |
| D14 | 5 | With no flags the AI recommendation is "No flags raised" (was "Eligible"); any flag gives "Requires Doctor Review" (HIGH for a likely deferral, MEDIUM for review). | The rules only flag; "Eligible" sounded like a decision. |
| D15 | 5 | All of question 7 (pregnancy and risk behaviour) is confidential and rules-only (never sent to an LLM). | Conflict 8, accepted. |
| D16 | 5 | "Which medicines?" is an optional part after "Yes" to medicines (not required, no extra flag beyond Review). | The doctor needs the name; asking it is not a new question. |
| D17 | 5 | Interview sessions from the old 12-section questionnaire restart with the 7 questions the next time the donor opens them (their old answers are not reused). Submitted old reports stay as they are, and "Update my answers" on them still uses the chat. | The old answers do not map to the new fields. |
| D18 | 5 | If the agent accepted the check but then could not take the edited answers (very unlikely), the old version stays superseded and the donor is told to use "Continue screening" (the chat) to send them. | A decided or superseded report version can never be changed back (immutability rule). |
| D19 | 5 | The Supervisor knowledge base was not built: there is no Gemini key in `Agents/Supervisor/.env`, and copying the Notification agent's key there is the owner's decision. | Secrets are not copied between files. |
