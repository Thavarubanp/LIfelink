using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.Entities;
using LifeLink.DTOs.Planning;
using LifeLink.Services.Common;
using LifeLink.Services.Inventory;
using LifeLink.Services.Notification;
using LifeLink.Services.Planning;
using LifeLink.Services.BloodCompatibility;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Acceptances
{
    /// <summary>
    /// Donor side of a blood request: accept → AI screening (report versions) → doctor approval reserves a slot
    /// → recorded donation fills it. Withdrawal or release frees a reserved slot. FulfilledUnits counts recorded
    /// donations only; ReservedUnits counts approved donors who have not donated yet.
    /// Hospitals can also donate from inventory: they accept with selected packets (held, no AI screening) and the
    /// request's assigned doctor approves, which fulfils the units at once, or rejects, which returns the packets.
    /// </summary>
    public class AcceptanceService : IAcceptanceService
    {
        public const string FulfilledByOthersReason = "Required donor count has been fulfilled by other selected donors.";
        public const string HospitalOfferConflictMessage = "This request or the selected packets were just changed by someone else. Please refresh and try again.";
        private const int MaxReportJsonLength = 200_000;

        private readonly AppDbContext _context;
        private readonly IBloodCompatibilityService _bloodCompatibilityService;
        private readonly IPlanningAgentService? _planningAgent;
        private readonly IScreeningAgentClient? _screeningAgent;

        public AcceptanceService(
            AppDbContext context,
            IBloodCompatibilityService bloodCompatibilityService,
            IPlanningAgentService? planningAgent = null,
            IScreeningAgentClient? screeningAgent = null)
        {
            _context = context;
            _bloodCompatibilityService = bloodCompatibilityService;
            _planningAgent = planningAgent;
            _screeningAgent = screeningAgent;
        }

        /// <summary>
        /// 7.3: the donor's own answers from their latest screening report version (no AI risk level, flags or summary).
        /// </summary>
        public async Task<ScreeningAnswersDto> GetScreeningAnswersAsync(Guid acceptanceId, Guid donorUserId)
        {
            var acceptance = await _context.Acceptances.FindAsync(acceptanceId)
                             ?? throw new KeyNotFoundException($"Acceptance with ID {acceptanceId} was not found.");
            if (acceptance.DonorUserId != donorUserId || acceptance.DonorHospitalId != null)
            {
                throw new UnauthorizedAccessException("Only the donor can see their screening answers.");
            }
            var latest = await _context.DonorVerifications.Where(v => v.AcceptanceId == acceptanceId)
                             .OrderByDescending(v => v.ReportVersion).FirstOrDefaultAsync()
                         ?? throw new KeyNotFoundException("No screening answers have been submitted for this donation yet.");
            var request = await RequireRequestAsync(acceptance.BloodRequestId);

            var dto = new ScreeningAnswersDto
            {
                AcceptanceId = acceptanceId, ReportVersion = latest.ReportVersion, Status = latest.Status.ToString(), SubmittedAt = latest.CreatedAt
            };
            try
            {
                using var doc = JsonDocument.Parse(latest.ReportJson ?? "{}");
                var root = doc.RootElement;
                if (root.TryGetProperty("questionnaire", out var questionnaire)) dto.Questionnaire = questionnaire.Clone();
                if (root.TryGetProperty("form_answers", out var formAnswers)) dto.Answers = formAnswers.Clone();
                if (root.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in sections.EnumerateArray())
                    {
                        var section = new ScreeningAnswerSectionDto
                        {
                            Index = s.TryGetProperty("index", out var i) && i.TryGetInt32(out var n) ? n : 0,
                            Title = s.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty,
                            Confidential = s.TryGetProperty("confidential", out var c) && c.ValueKind == JsonValueKind.True
                        };
                        if (s.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in items.EnumerateArray())
                            {
                                section.Items.Add(new ScreeningAnswerItemDto
                                {
                                    Question = item.TryGetProperty("question", out var q) ? q.GetString() ?? string.Empty : string.Empty,
                                    Answer = item.TryGetProperty("answer", out var a) ? a.ToString() : string.Empty
                                });
                            }
                        }
                        dto.Sections.Add(section);
                    }
                }
            }
            catch (JsonException)
            {
                // A legacy record without a structured report: nothing to show
            }

            if (acceptance.Status != AcceptanceStatus.ScreeningCompleted || latest.Status != VerificationStatus.Pending)
            {
                dto.EditUnavailableReason = "The doctor has already decided on these answers, or they are not waiting for review.";
            }
            else if (SuspensionGuard.IsSuspended(request))
            {
                dto.EditUnavailableReason = "The blood request is temporarily suspended by the administrator.";
            }
            else if (dto.Questionnaire == null)
            {
                dto.EditUnavailableReason = "These answers come from the earlier questionnaire; use Continue screening in the chat to update them.";
            }
            dto.CanEdit = dto.EditUnavailableReason == null;
            return dto;
        }

        /// <summary>
        /// 7.2: the donor saves the edit form while their report waits for the doctor. The agent checks the answers first
        /// (nothing changes if they are incomplete or the agent is unreachable); then the current version is superseded and
        /// the agent builds and submits the new version in the background. The chat interview does not run again.
        /// </summary>
        public async Task<AcceptanceResponseDto> UpdateScreeningAnswersAsync(Guid acceptanceId, Guid donorUserId, JsonElement answers)
        {
            if (answers.ValueKind != JsonValueKind.Object || answers.GetRawText().Length > 20_000)
            {
                throw new InvalidOperationException("The answers are missing or too large.");
            }
            if (_screeningAgent == null)
            {
                throw new ScreeningAgentUnavailableException("The screening assistant is not configured.");
            }

            var current = await GetScreeningAnswersAsync(acceptanceId, donorUserId);
            if (!current.CanEdit)
            {
                throw new InvalidOperationException(current.EditUnavailableReason ?? "These answers can no longer be changed.");
            }
            await DonorEligibility.RequireEligibleDonorAccountAsync(_context, donorUserId);

            var problems = await _screeningAgent.ValidateAnswersAsync(acceptanceId, answers);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(string.Join(" ", problems));
            }

            // Supersede the waiting version and reopen (the same rules and notifications as "Update my answers")
            var result = await ReopenScreeningAsync(acceptanceId, donorUserId);
            await ActivityLogger.AddAsync(_context, donorUserId, "Screening.AnswersEdited", ActivityLogger.Types.Screening, acceptanceId,
                "Edited their screening answers with the form (a new report version follows).");
            await _context.SaveChangesAsync();

            if (!await _screeningAgent.SubmitAnswersAsync(acceptanceId, answers))
            {
                // The donor can still finish in the chat (Continue screening): their previous answers are the defaults there
                throw new ScreeningAgentUnavailableException(
                    "Your previous answers were withdrawn for editing, but the new answers could not be sent right now. Use Continue screening to send them.");
            }
            return result;
        }

        public async Task<AcceptanceResponseDto> AcceptRequestAsync(Guid donorUserId, CreateAcceptanceDto dto)
        {
            if (donorUserId == Guid.Empty)
            {
                throw new ArgumentException("Invalid donor user ID.");
            }

            if (dto.BloodRequestId == Guid.Empty)
            {
                throw new ArgumentException("BloodRequestId is required.");
            }

            // Governance: only active, non-suspended, non-blocked donor accounts take part
            var donor = await DonorEligibility.RequireEligibleDonorAccountAsync(_context, donorUserId);

            var request = await _context.BloodRequests.FindAsync(dto.BloodRequestId);
            if (request == null)
            {
                throw new InvalidOperationException($"Blood request with ID {dto.BloodRequestId} was not found.");
            }
            SuspensionGuard.EnsureNotSuspended(request);

            if (request.CancelledAt != null || request.Status == BloodRequestStatus.Cancelled || request.Status == BloodRequestStatus.Deleted)
            {
                throw new InvalidOperationException("Cannot accept a cancelled blood request.");
            }

            if (request.Status == BloodRequestStatus.Completed)
            {
                throw new InvalidOperationException("Cannot accept a completed blood request.");
            }

            if (request.FulfilledUnits >= request.UnitsRequired)
            {
                throw new InvalidOperationException("This blood request has already been fulfilled.");
            }

            if (request.Status != BloodRequestStatus.Approved)
            {
                throw new InvalidOperationException("Only approved blood requests can be accepted.");
            }

            if (await _context.Hospitals.AnyAsync(h => h.HospitalId == request.HospitalId && h.IsSuspended))
            {
                throw new InvalidOperationException("The hospital for this request is suspended, so it cannot accept donors.");
            }

            // Visible but paused while every remaining slot is reserved by an approved donor
            if (request.FulfilledUnits + request.ReservedUnits >= request.UnitsRequired)
            {
                throw new InvalidOperationException("All remaining donation slots are reserved. New acceptances are paused until a slot is released.");
            }

            if (request.PatientUserId == donorUserId)
            {
                throw new InvalidOperationException("Donors cannot accept their own blood requests.");
            }

            var hasAccepted = await _context.Acceptances.AnyAsync(a =>
                a.BloodRequestId == dto.BloodRequestId &&
                a.DonorUserId == donorUserId &&
                a.Status != AcceptanceStatus.Cancelled);
            if (hasAccepted)
            {
                throw new InvalidOperationException("Donor has already accepted this blood request.");
            }

            // Blood group: a donation-confirmed group always wins; otherwise the declared group is saved to the profile
            var bloodGroup = await ResolveDonorBloodGroupAsync(donor, dto.DonorBloodGroup);
            if (!_bloodCompatibilityService.IsCompatible(bloodGroup, request.BloodGroup))
            {
                throw new InvalidOperationException(
                    $"Donor blood group '{bloodGroup}' is not compatible with recipient blood group '{request.BloodGroup}'.");
            }

            var now = DateTime.UtcNow;
            if (!DonorEligibility.IsIntervalSatisfied(donor, now))
            {
                throw new InvalidOperationException(
                    $"At least {DonorEligibility.DonationIntervalDays} days must pass between donations. You can donate again from {DonorEligibility.NextEligibleDate(donor):yyyy-MM-dd}.");
            }

            if (!DonorEligibility.IsAgeEligible(donor.DateOfBirth, now))
            {
                throw new InvalidOperationException(
                    $"Blood donors must be between {DonorEligibility.MinimumAge} and {DonorEligibility.MaximumAge} years old.");
            }

            // One active donation process per donor (a completed donation is covered by the 120-day interval)
            var hasActiveProcess = await _context.Acceptances.AnyAsync(a =>
                a.DonorUserId == donorUserId &&
                (a.Status == AcceptanceStatus.Accepted ||
                 a.Status == AcceptanceStatus.ScreeningPending ||
                 a.Status == AcceptanceStatus.Verified ||
                 a.Status == AcceptanceStatus.ScreeningCompleted));
            if (hasActiveProcess)
            {
                throw new ConflictException("You already have an active donation process.");
            }

            var acceptance = new Acceptance
            {
                AcceptanceId = Guid.NewGuid(),
                BloodRequestId = dto.BloodRequestId,
                DonorUserId = donorUserId,
                Status = AcceptanceStatus.Accepted,
                AcceptedAt = now
            };

            await _context.Acceptances.AddAsync(acceptance);
            // Saving the acceptance also moves the request's token (AppDbContext): a cancel, expiry, completion or a second
            // accept at the same moment makes one of the two fail with 409 instead of leaving a stranded acceptance.
            await ActivityLogger.AddAsync(_context, donorUserId, "Donation.Accepted", ActivityLogger.Types.Donation, acceptance.AcceptanceId,
                $"Accepted blood request #{NotificationFactory.ShortId(request.BloodRequestId)} ({request.BloodGroup}) as a donor (blood group {bloodGroup}).");
            await SaveNewAcceptanceAsync(() => _context.SaveChangesAsync());

            // Supervisor workflow: DonorAccepted → Request Management agent opens the screening interview
            if (_planningAgent != null)
            {
                await _planningAgent.DispatchPlanAsync(new PlanRequestDto
                {
                    EventType = "DonorAccepted",
                    RequestId = acceptance.BloodRequestId.ToString(),
                    DonorId = acceptance.DonorUserId.ToString(),
                    Payload = new Dictionary<string, object>
                    {
                        ["acceptanceId"] = acceptance.AcceptanceId.ToString(),
                        ["requestId"] = acceptance.BloodRequestId.ToString(),
                        ["bloodRequestId"] = acceptance.BloodRequestId.ToString(),
                        ["donorId"] = acceptance.DonorUserId.ToString(),
                        ["donorUserId"] = acceptance.DonorUserId.ToString()
                    }
                });
            }

            return MapToResponseDto(acceptance);
        }

        private async Task<string> ResolveDonorBloodGroupAsync(User donor, string? declared)
        {
            var hasDeclared = BloodValidationHelper.IsValidBloodGroup(declared);
            if (!string.IsNullOrWhiteSpace(donor.BloodGroup) && await DonorEligibility.IsBloodGroupConfirmedAsync(_context, donor.UserId))
            {
                if (hasDeclared && !string.Equals(BloodValidationHelper.NormalizeBloodGroup(declared!), donor.BloodGroup, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Your blood group was confirmed as {donor.BloodGroup} at a previous donation.");
                }
                return donor.BloodGroup;
            }

            if (!hasDeclared)
            {
                if (!string.IsNullOrWhiteSpace(donor.BloodGroup)) return donor.BloodGroup;
                throw new InvalidOperationException("Please select your blood group before accepting a request.");
            }

            var normalized = BloodValidationHelper.NormalizeBloodGroup(declared!);
            donor.BloodGroup = normalized;
            donor.UpdatedAt = DateTime.UtcNow;
            return normalized;
        }

        /// <summary>Donor withdraws. Before approval nothing else changes; after approval the reserved slot is released.</summary>
        public async Task<AcceptanceResponseDto> CancelAcceptanceAsync(Guid acceptanceId, Guid donorUserId)
        {
            var acceptance = await _context.Acceptances.FindAsync(acceptanceId);
            if (acceptance == null)
            {
                throw new KeyNotFoundException($"Acceptance with ID {acceptanceId} was not found.");
            }

            if (acceptance.DonorUserId != donorUserId)
            {
                throw new InvalidOperationException("Only the accepting donor can cancel this acceptance.");
            }

            if (acceptance.Status == AcceptanceStatus.Cancelled)
            {
                throw new InvalidOperationException("Acceptance is already cancelled.");
            }

            if (acceptance.Status == AcceptanceStatus.Matched)
            {
                throw new InvalidOperationException("Cannot cancel a matched acceptance.");
            }

            if (!AcceptanceClosure.IsActive(acceptance.Status))
            {
                throw new InvalidOperationException($"This acceptance is already closed ({acceptance.Status}).");
            }

            var request = await RequireRequestAsync(acceptance.BloodRequestId);
            var wasReserved = acceptance.Status == AcceptanceStatus.Verified;

            await AcceptanceClosure.CloseAsync(_context, acceptance, request, AcceptanceStatus.Cancelled, null, "Donor withdrew.");

            await NotifyWithdrawalRecipientsAsync(request, "DonorWithdrew", "Donor Withdrew",
                $"A donor withdrew from blood request #{NotificationFactory.ShortId(request.BloodRequestId)} ({request.BloodGroup})." +
                (wasReserved ? " Their reserved slot is free again." : string.Empty));

            await ActivityLogger.AddAsync(_context, donorUserId, "Donation.Withdrawn", ActivityLogger.Types.Donation, acceptance.AcceptanceId,
                $"Withdrew from blood request #{NotificationFactory.ShortId(request.BloodRequestId)} ({request.BloodGroup}).");
            await _context.SaveChangesAsync();
            return MapToResponseDto(acceptance);
        }

        /// <summary>
        /// A doctor or the hospital's staff ends an acceptance that cannot proceed (for example a donor who did not
        /// attend or whose account was suspended). A reserved slot becomes free again.
        /// </summary>
        public async Task<AcceptanceResponseDto> ReleaseReservationAsync(Guid acceptanceId, Guid actorUserId, Guid? actingHospitalId, string? reason)
        {
            var message = RequireReason(reason, "A reason is required to release a reservation.");
            var acceptance = await _context.Acceptances.FindAsync(acceptanceId)
                             ?? throw new KeyNotFoundException($"Acceptance with ID {acceptanceId} was not found.");
            var request = await RequireRequestAsync(acceptance.BloodRequestId);
            SuspensionGuard.EnsureNotSuspended(request);
            await RequireHospitalAuthorityAsync(request, actorUserId, actingHospitalId);

            if (acceptance.DonorHospitalId != null)
            {
                throw new InvalidOperationException("Hospital donation offers are approved or rejected by the assigned doctor.");
            }

            if (!AcceptanceClosure.IsActive(acceptance.Status))
            {
                throw new InvalidOperationException($"Only active acceptances can be released. This one is {acceptance.Status}.");
            }

            await AcceptanceClosure.CloseAsync(_context, acceptance, request, AcceptanceStatus.Cancelled,
                $"Released by the hospital: {message}", $"Released by the hospital: {message}");

            await _context.Notifications.AddAsync(NotificationFactory.ForUser(acceptance.DonorUserId, "Donor", "DonationReservationReleased",
                "Donation Reservation Released",
                $"Your donation for blood request #{NotificationFactory.ShortId(request.BloodRequestId)} was released by the hospital. Reason: {message}"));

            await ActivityLogger.AddAsync(_context, actorUserId, "Donation.Released", ActivityLogger.Types.Donation, acceptance.AcceptanceId,
                $"Released a donor from blood request #{NotificationFactory.ShortId(request.BloodRequestId)}: {message}", request.HospitalId, acceptance.DonorUserId);
            await _context.SaveChangesAsync();
            return MapToResponseDto(acceptance);
        }

        /// <summary>
        /// Donor chooses "Update my answers" while the latest report still awaits the doctor: that version becomes
        /// Superseded (kept) and the interview reopens; the next submission is a new version.
        /// </summary>
        public async Task<AcceptanceResponseDto> ReopenScreeningAsync(Guid acceptanceId, Guid donorUserId)
        {
            var acceptance = await _context.Acceptances.FindAsync(acceptanceId)
                             ?? throw new KeyNotFoundException($"Acceptance with ID {acceptanceId} was not found.");
            if (acceptance.DonorUserId != donorUserId)
            {
                throw new UnauthorizedAccessException("Only the donor can update their screening answers.");
            }

            await DonorEligibility.RequireEligibleDonorAccountAsync(_context, donorUserId);

            if (acceptance.Status != AcceptanceStatus.ScreeningCompleted)
            {
                throw new InvalidOperationException("Answers can only be updated while the report is waiting for the doctor.");
            }

            var latest = await _context.DonorVerifications
                .Where(v => v.AcceptanceId == acceptanceId)
                .OrderByDescending(v => v.ReportVersion)
                .FirstOrDefaultAsync();
            if (latest == null || latest.Status != VerificationStatus.Pending)
            {
                throw new InvalidOperationException("The doctor has already decided on this report, so it can no longer be updated.");
            }

            latest.Status = VerificationStatus.Superseded;
            latest.Notes = "Donor chose to update their answers.";
            latest.UpdatedAt = DateTime.UtcNow;
            acceptance.Status = AcceptanceStatus.ScreeningPending;

            var request = await RequireRequestAsync(acceptance.BloodRequestId);
            SuspensionGuard.EnsureNotSuspended(request);
            await NotifyEligibleDoctorOrHospitalAsync(request, latest.DoctorId, "ScreeningReportSuperseded", "Screening Report Being Updated",
                $"The donor is updating their screening answers for request #{NotificationFactory.ShortId(request.BloodRequestId)}. A new report version will follow.");

            await ActivityLogger.AddAsync(_context, donorUserId, "Screening.Reopened", ActivityLogger.Types.Screening, acceptance.AcceptanceId,
                $"Reopened their screening answers for blood request #{NotificationFactory.ShortId(request.BloodRequestId)} (a new report version will follow).");
            await _context.SaveChangesAsync();
            return MapToResponseDto(acceptance);
        }

        public async Task<IEnumerable<AcceptanceResponseDto>> GetMyAcceptancesAsync(Guid donorUserId)
        {
            var acceptances = await _context.Acceptances
                .Where(a => a.DonorUserId == donorUserId)
                .OrderByDescending(a => a.AcceptedAt)
                .ToListAsync();
            if (acceptances.Count == 0) return new List<AcceptanceResponseDto>();

            var requestIds = acceptances.Select(a => a.BloodRequestId).Distinct().ToList();
            var requests = await _context.BloodRequests
                .Where(r => requestIds.Contains(r.BloodRequestId))
                .ToDictionaryAsync(r => r.BloodRequestId);
            var hospitalIds = requests.Values.Select(r => r.HospitalId).Distinct().ToList();
            var hospitalNames = await _context.Hospitals
                .Where(h => hospitalIds.Contains(h.HospitalId))
                .ToDictionaryAsync(h => h.HospitalId, h => h.Name);

            var acceptanceIds = acceptances.Select(a => a.AcceptanceId).ToList();
            var reports = await _context.DonorVerifications
                .Include(v => v.DecidedByDoctor)
                .Where(v => acceptanceIds.Contains(v.AcceptanceId))
                .OrderBy(v => v.ReportVersion)
                .ToListAsync();

            return acceptances.Select(a =>
            {
                var dto = MapToResponseDto(a);
                if (requests.TryGetValue(a.BloodRequestId, out var r))
                {
                    AddRequestContext(dto, r, hospitalNames);
                }
                dto.ScreeningHistory = reports.Where(v => v.AcceptanceId == a.AcceptanceId).Select(MapDecision).ToList();
                return dto;
            }).ToList();
        }

        public static ScreeningDecisionDto MapDecision(DonorVerification v) => new()
        {
            DonorVerificationId = v.DonorVerificationId,
            ReportVersion = v.ReportVersion,
            Status = v.Status.ToString(),
            SubmittedAt = v.CreatedAt,
            DecidedAt = v.Status is VerificationStatus.Approved or VerificationStatus.Rejected ? v.VerifiedAt : null,
            DecidedByName = v.Status is VerificationStatus.Approved or VerificationStatus.Rejected
                ? (v.DecidedByDoctor != null && v.DecidedByDoctor.DeletedAt == null ? $"Dr. {v.DecidedByDoctor.FirstName} {v.DecidedByDoctor.LastName}".Trim() : "Removed doctor")
                : null,
            ApprovalNotes = v.Status == VerificationStatus.Approved ? v.Notes : null,
            RejectionReason = v.Status == VerificationStatus.Rejected ? v.Notes : null,
            Note = v.Status is VerificationStatus.Superseded or VerificationStatus.Closed ? v.Notes : null
        };

        public async Task<AcceptanceResponseDto?> GetAcceptanceByIdAsync(Guid acceptanceId)
        {
            var acceptance = await _context.Acceptances.FindAsync(acceptanceId);
            if (acceptance == null) return null;
            var dto = MapToResponseDto(acceptance);
            // Read by the screening agent, which pauses the interview while the request is suspended
            dto.RequestSuspended = await _context.BloodRequests
                .AnyAsync(r => r.BloodRequestId == acceptance.BloodRequestId && r.AdminSuspendedAt != null);
            return dto;
        }

        public async Task<List<RequestAcceptanceDetailDto>> GetRequestAcceptancesAsync(Guid bloodRequestId)
        {
            var acceptances = await _context.Acceptances
                .Where(a => a.BloodRequestId == bloodRequestId)
                .OrderBy(a => a.AcceptedAt)
                .ToListAsync();

            var packets = await PacketsByAcceptanceAsync(acceptances.Where(a => a.DonorHospitalId != null).Select(a => a.AcceptanceId).ToList());

            var result = new List<RequestAcceptanceDetailDto>();
            foreach (var a in acceptances)
            {
                if (a.DonorHospitalId != null)
                {
                    var hospital = await _context.Hospitals.FindAsync(a.DonorHospitalId.Value);
                    result.Add(new RequestAcceptanceDetailDto
                    {
                        AcceptanceId = a.AcceptanceId,
                        BloodRequestId = a.BloodRequestId,
                        DonorUserId = a.DonorUserId,
                        DonorName = hospital?.Name ?? "Hospital",
                        DonorEmail = hospital?.Email ?? string.Empty,
                        DonorPhoneNumber = hospital?.ContactNumber ?? string.Empty,
                        Status = a.Status.ToString(),
                        AcceptedAt = a.AcceptedAt,
                        RejectionReason = a.RejectionReason,
                        DonorHospitalId = a.DonorHospitalId,
                        Packets = packets.GetValueOrDefault(a.AcceptanceId) ?? new List<DonatedPacketDto>()
                    });
                    continue;
                }

                var user = await _context.Users.FindAsync(a.DonorUserId);
                result.Add(new RequestAcceptanceDetailDto
                {
                    AcceptanceId = a.AcceptanceId,
                    BloodRequestId = a.BloodRequestId,
                    DonorUserId = a.DonorUserId,
                    DonorName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : string.Empty,
                    DonorEmail = user?.Email ?? string.Empty,
                    DonorPhoneNumber = user?.PhoneNumber ?? string.Empty,
                    Status = a.Status.ToString(),
                    AcceptedAt = a.AcceptedAt,
                    RejectionReason = a.RejectionReason
                });
            }

            return result;
        }

        /// <summary>
        /// Record donations: approved (Verified) donors who actually donated. Each one moves a reserved slot to
        /// FulfilledUnits, confirms the tested blood group, starts the donor's 120-day interval and, for
        /// hospital-created requests, adds a traceable packet to that hospital's stock. The request completes only
        /// when FulfilledUnits reaches UnitsRequired; donors still in screening are then closed.
        /// Performed by an active doctor of the hospital or by that hospital's staff (actingHospitalId).
        /// </summary>
        public async Task<FinalizeDonorSelectionResponseDto> FinalizeDonorSelectionAsync(
            Guid bloodRequestId,
            List<Guid> selectedAcceptanceIds,
            Guid actorUserId,
            Guid? actingHospitalId = null,
            Dictionary<Guid, string>? testedBloodGroups = null)
        {
            if (selectedAcceptanceIds == null || selectedAcceptanceIds.Count == 0)
            {
                throw new ArgumentException("At least one donor must be selected.");
            }

            var request = await _context.BloodRequests.FindAsync(bloodRequestId);
            if (request == null)
            {
                throw new InvalidOperationException($"Blood request with ID {bloodRequestId} was not found.");
            }
            SuspensionGuard.EnsureNotSuspended(request);

            if (request.Status == BloodRequestStatus.Completed)
            {
                throw new InvalidOperationException("Cannot record donations on a completed blood request.");
            }

            if (request.Status != BloodRequestStatus.Approved)
            {
                throw new InvalidOperationException("Donations can only be recorded for approved blood requests.");
            }

            await RequireHospitalAuthorityAsync(request, actorUserId, actingHospitalId);

            var selectedSet = selectedAcceptanceIds.ToHashSet();
            var selected = await _context.Acceptances
                .Where(a => a.BloodRequestId == bloodRequestId && selectedSet.Contains(a.AcceptanceId))
                .ToListAsync();

            if (selected.Count != selectedSet.Count)
            {
                throw new InvalidOperationException("One or more selected acceptance IDs do not belong to this blood request.");
            }

            if (selected.Any(a => a.Status != AcceptanceStatus.Verified))
            {
                throw new InvalidOperationException("Only donors approved by a doctor (with a reserved slot) can have a donation recorded.");
            }

            var createdByHospital = await _context.UserRoles
                .AnyAsync(ur => ur.UserId == request.PatientUserId && ur.Role.Name == "HospitalStaff");
            var now = DateTime.UtcNow;

            foreach (var acceptance in selected)
            {
                var donor = await _context.Users.FindAsync(acceptance.DonorUserId)
                            ?? throw new InvalidOperationException("Donor account was not found.");
                if (!DonorEligibility.IsActiveAccount(donor))
                {
                    throw new InvalidOperationException("A selected donor's account is no longer active. Release that reservation instead.");
                }

                var testedGroup = testedBloodGroups != null && testedBloodGroups.TryGetValue(acceptance.AcceptanceId, out var g) ? g : donor.BloodGroup;
                if (!BloodValidationHelper.IsValidBloodGroup(testedGroup))
                {
                    throw new InvalidOperationException("Confirm the tested blood group for every donation.");
                }
                testedGroup = BloodValidationHelper.NormalizeBloodGroup(testedGroup!);
                if (!_bloodCompatibilityService.IsCompatible(testedGroup, request.BloodGroup))
                {
                    throw new InvalidOperationException(
                        $"Tested blood group {testedGroup} is not compatible with {request.BloodGroup}. Release this reservation instead.");
                }

                acceptance.Status = AcceptanceStatus.Matched;
                donor.BloodGroup = testedGroup;
                donor.LastDonationDate = now;
                donor.UpdatedAt = now;

                await _context.RequestFulfillmentHistories.AddAsync(new RequestFulfillmentHistory
                {
                    Id = Guid.NewGuid(),
                    BloodRequestId = bloodRequestId,
                    AcceptanceId = acceptance.AcceptanceId,
                    DonorUserId = acceptance.DonorUserId,
                    FulfilledAt = now
                });

                // Hospital-created requests restock that hospital: the donated unit becomes a traceable packet
                if (createdByHospital)
                {
                    await InventoryLedger.AddCollectedPacketsAsync(_context, request.HospitalId, testedGroup, 1, now,
                        BloodPacketSource.Donation, acceptance.AcceptanceId, TransactionType.DonationCollected,
                        $"Donation recorded for blood request {request.BloodRequestId}", actorUserId);
                }

                await _context.Notifications.AddAsync(NotificationFactory.ForUser(acceptance.DonorUserId, "Donor", "DonationRecorded",
                    "Donation Recorded",
                    $"Thank you! Your donation for blood request #{NotificationFactory.ShortId(bloodRequestId)} was recorded. You can donate again after {DonorEligibility.DonationIntervalDays} days."));
            }

            request.ReservedUnits = Math.Max(0, request.ReservedUnits - selected.Count);
            request.FulfilledUnits += selected.Count;
            request.UpdatedAt = now;

            var closedCount = await CompleteIfFulfilledAsync(request);
            await NotifyExternalCreatorOfDonationProgressAsync(request, createdByHospital);

            await ActivityLogger.AddAsync(_context, actorUserId, "Donation.Recorded", ActivityLogger.Types.Donation, request.BloodRequestId,
                $"Recorded {selected.Count} donation(s) for blood request #{NotificationFactory.ShortId(request.BloodRequestId)} ({request.FulfilledUnits}/{request.UnitsRequired} donated).", request.HospitalId);
            await _context.SaveChangesAsync();

            return new FinalizeDonorSelectionResponseDto
            {
                MatchedDonors = selected.Count,
                RejectedDonors = closedCount,
                TotalFulfilledUnits = request.FulfilledUnits,
                RemainingUnits = Math.Max(0, request.UnitsRequired - request.FulfilledUnits),
                ReservedUnits = request.ReservedUnits,
                Message = "Donations recorded successfully."
            };
        }

        /// <summary>
        /// When FulfilledUnits reaches UnitsRequired the request completes and everyone still in screening (donors and
        /// pending hospital offers, whose packets return to their inventory) is closed and told. Returns how many closed.
        /// </summary>
        private async Task<int> CompleteIfFulfilledAsync(BloodRequest request)
        {
            if (request.FulfilledUnits < request.UnitsRequired)
            {
                return 0;
            }

            request.Status = BloodRequestStatus.Completed;
            var closed = await AcceptanceClosure.CloseAllAsync(_context, request, AcceptanceClosure.InScreeningStatuses,
                AcceptanceStatus.Rejected, FulfilledByOthersReason, "Request fulfilled before a decision was needed.");
            foreach (var a in closed)
            {
                await _context.Notifications.AddAsync(a.DonorHospitalId != null
                    ? NotificationFactory.ForHospital(a.DonorHospitalId.Value, "RequestFulfilled", "Blood Request Fulfilled",
                        $"Blood request #{NotificationFactory.ShortId(request.BloodRequestId)} was fulfilled before your donation was approved. The reserved packets are back in your inventory.")
                    : NotificationFactory.ForUser(a.DonorUserId, "Donor", "RequestFulfilled",
                        "Blood Request Fulfilled",
                        $"Blood request #{NotificationFactory.ShortId(request.BloodRequestId)} has been fulfilled by other donors. Thank you for offering to help."));
            }
            return closed.Count;
        }

        /// <summary>
        /// A hospital accepts a public blood request by donating selected Available packets of the required group from
        /// its own inventory. The packets are held for this offer. No AI agent runs: the request's assigned doctor
        /// approves or rejects. A hospital cannot donate to a request sent to itself (it issues from stock instead).
        /// </summary>
        public async Task<AcceptanceResponseDto> AcceptAsHospitalAsync(Guid staffUserId, Guid hospitalId, CreateHospitalDonationDto dto)
        {
            if (dto.BloodRequestId == Guid.Empty)
            {
                throw new ArgumentException("BloodRequestId is required.");
            }

            var donorHospital = await _context.Hospitals.FindAsync(hospitalId)
                                ?? throw new InvalidOperationException("Your hospital was not found.");
            if (!donorHospital.IsVerified || donorHospital.IsSuspended)
            {
                throw new InvalidOperationException("Only approved hospitals that are not suspended can donate blood.");
            }

            var request = await _context.BloodRequests.FindAsync(dto.BloodRequestId)
                          ?? throw new InvalidOperationException($"Blood request with ID {dto.BloodRequestId} was not found.");
            SuspensionGuard.EnsureNotSuspended(request);

            if (request.HospitalId == hospitalId)
            {
                throw new InvalidOperationException("Your hospital cannot donate to a blood request sent to your own hospital. Issue the packets from your inventory instead.");
            }

            if (request.CancelledAt != null || request.Status == BloodRequestStatus.Cancelled || request.Status == BloodRequestStatus.Deleted)
            {
                throw new InvalidOperationException("Cannot accept a cancelled blood request.");
            }

            if (request.Status != BloodRequestStatus.Approved)
            {
                throw new InvalidOperationException("Only approved blood requests can be accepted.");
            }

            if (await _context.Hospitals.AnyAsync(h => h.HospitalId == request.HospitalId && h.IsSuspended))
            {
                throw new InvalidOperationException("The hospital for this request is suspended, so it cannot accept donations.");
            }

            if (await _context.Acceptances.AnyAsync(a => a.BloodRequestId == request.BloodRequestId && a.DonorHospitalId == hospitalId &&
                                                         a.Status == AcceptanceStatus.Accepted))
            {
                throw new InvalidOperationException("Your hospital already has a donation waiting for the doctor on this request.");
            }

            var freeSlots = request.UnitsRequired - request.FulfilledUnits - request.ReservedUnits;
            if (freeSlots <= 0)
            {
                throw new InvalidOperationException("All remaining donation slots are reserved. New acceptances are paused until a slot is released.");
            }

            var packets = await InventoryLedger.RequireSelectablePacketsAsync(_context, hospitalId, dto.PacketIds, request.BloodGroup);
            if (packets.Count > freeSlots)
            {
                throw new InvalidOperationException($"This request needs at most {freeSlots} more unit(s); {packets.Count} packets selected.");
            }

            var acceptance = new Acceptance
            {
                AcceptanceId = Guid.NewGuid(),
                BloodRequestId = request.BloodRequestId,
                DonorUserId = staffUserId,
                DonorHospitalId = hospitalId,
                Status = AcceptanceStatus.Accepted,
                AcceptedAt = DateTime.UtcNow
            };
            await _context.Acceptances.AddAsync(acceptance);
            await InventoryLedger.ReservePacketsAsync(_context, packets, acceptance.AcceptanceId,
                $"Held for donation to blood request #{NotificationFactory.ShortId(request.BloodRequestId)}", staffUserId);

            await NotifyRequestStaffAsync(request, "HospitalDonationOffered", "Hospital Donation Offer",
                $"{donorHospital.Name} offers {packets.Count} {request.BloodGroup} packet(s) for blood request #{NotificationFactory.ShortId(request.BloodRequestId)}. Review and approve or reject the offer.");

            // No Supervisor / agent call: hospital blood is not screened. Saving the offer also moves the request's token
            // (AppDbContext), so an offer and a cancel / expiry / completion at the same moment cannot both succeed.
            await ActivityLogger.AddForHospitalAsync(_context, hospitalId, "Donation.HospitalOffered", ActivityLogger.Types.Donation, acceptance.AcceptanceId,
                $"Offered {packets.Count} {request.BloodGroup} packet(s) from inventory to blood request #{NotificationFactory.ShortId(request.BloodRequestId)}.");
            await SaveNewAcceptanceAsync(() => InventoryLedger.SavePacketChangesAsync(_context, HospitalOfferConflictMessage));

            return (await MapHospitalDonationsAsync(new List<Acceptance> { acceptance })).Single();
        }

        /// <summary>The donating hospital withdraws its offer while it waits for the doctor; the packets return.</summary>
        public async Task<AcceptanceResponseDto> WithdrawHospitalDonationAsync(Guid acceptanceId, Guid hospitalId)
        {
            var acceptance = await _context.Acceptances.FindAsync(acceptanceId)
                             ?? throw new KeyNotFoundException($"Acceptance with ID {acceptanceId} was not found.");
            if (acceptance.DonorHospitalId != hospitalId)
            {
                throw new UnauthorizedAccessException("Only the hospital that offered this donation can withdraw it.");
            }

            if (acceptance.Status != AcceptanceStatus.Accepted)
            {
                throw new InvalidOperationException($"Only donations waiting for the doctor can be withdrawn. This one is {acceptance.Status}.");
            }

            var request = await RequireRequestAsync(acceptance.BloodRequestId);
            await AcceptanceClosure.CloseAsync(_context, acceptance, request, AcceptanceStatus.Cancelled, null, "Hospital withdrew its donation.");
            await NotifyRequestStaffAsync(request, "HospitalDonationWithdrawn", "Hospital Donation Withdrawn",
                $"A hospital withdrew its donation offer for blood request #{NotificationFactory.ShortId(request.BloodRequestId)}.");

            await ActivityLogger.AddForHospitalAsync(_context, hospitalId, "Donation.HospitalWithdrawn", ActivityLogger.Types.Donation, acceptance.AcceptanceId,
                $"Withdrew its donation offer for blood request #{NotificationFactory.ShortId(request.BloodRequestId)} (packets back in inventory).");
            await InventoryLedger.SavePacketChangesAsync(_context);
            return (await MapHospitalDonationsAsync(new List<Acceptance> { acceptance })).Single();
        }

        /// <summary>Donations offered by a hospital (newest first), with the request context and packets.</summary>
        public async Task<IEnumerable<AcceptanceResponseDto>> GetHospitalDonationsAsync(Guid hospitalId)
        {
            var acceptances = await _context.Acceptances
                .Where(a => a.DonorHospitalId == hospitalId)
                .OrderByDescending(a => a.AcceptedAt)
                .ToListAsync();
            return await MapHospitalDonationsAsync(acceptances);
        }

        /// <summary>
        /// The request's assigned doctor approves a hospital donation. The units count as fulfilled at once (the blood
        /// already exists): packets become Donated for a patient's request, or move into the requesting hospital's
        /// inventory when the request was created by a hospital. If the assigned doctor was removed, any active doctor
        /// of the requesting hospital may decide.
        /// </summary>
        public async Task<AcceptanceResponseDto> ApproveHospitalDonationAsync(Guid acceptanceId, Guid doctorUserId, string? notes)
        {
            var (acceptance, request, doctor) = await GetPendingHospitalDonationForDoctorAsync(acceptanceId, doctorUserId);

            if (request.Status != BloodRequestStatus.Approved)
            {
                throw new InvalidOperationException($"The blood request is {request.Status}, so donations can no longer be approved.");
            }

            var packets = await InventoryLedger.GetHeldPacketsAsync(_context, acceptance.AcceptanceId);
            if (packets.Count == 0)
            {
                throw new InvalidOperationException("No packets are reserved for this donation.");
            }

            var expired = packets.FirstOrDefault(p => p.ExpiryDate <= DateTime.UtcNow);
            if (expired != null)
            {
                throw new InvalidOperationException($"Packet {expired.TrackingNumber} expired while waiting. Reject this donation.");
            }

            var freeSlots = request.UnitsRequired - request.FulfilledUnits - request.ReservedUnits;
            if (packets.Count > freeSlots)
            {
                throw new InvalidOperationException(
                    $"Only {Math.Max(0, freeSlots)} donation slot(s) are free now; this donation has {packets.Count} packet(s). Reject it or wait for a reserved slot to be released.");
            }

            var now = DateTime.UtcNow;
            var donorHospitalName = await _context.Hospitals.Where(h => h.HospitalId == acceptance.DonorHospitalId).Select(h => h.Name).FirstOrDefaultAsync() ?? "A hospital";
            var requestHospitalName = await _context.Hospitals.Where(h => h.HospitalId == request.HospitalId).Select(h => h.Name).FirstOrDefaultAsync() ?? "the hospital";
            var shortId = NotificationFactory.ShortId(request.BloodRequestId);
            var createdByHospital = await _context.UserRoles
                .AnyAsync(ur => ur.UserId == request.PatientUserId && ur.Role.Name == "HospitalStaff");

            if (createdByHospital)
            {
                // A hospital's own request: the donated packets join its inventory (same tracking numbers)
                await InventoryLedger.MovePacketsAsync(_context, packets, request.HospitalId, acceptance.AcceptanceId,
                    TransactionType.Donated, TransactionType.DonationReceived,
                    $"Donated to {requestHospitalName} for blood request #{shortId}",
                    $"Received from {donorHospitalName} for blood request #{shortId}", doctorUserId);
            }
            else
            {
                await InventoryLedger.DonateHeldPacketsAsync(_context, packets, acceptance.AcceptanceId,
                    $"Donated for blood request #{shortId} at {requestHospitalName}", doctorUserId);
            }

            acceptance.Status = AcceptanceStatus.Matched;
            acceptance.RejectionReason = null;
            foreach (var _ in packets)
            {
                await _context.RequestFulfillmentHistories.AddAsync(new RequestFulfillmentHistory
                {
                    Id = Guid.NewGuid(),
                    BloodRequestId = request.BloodRequestId,
                    AcceptanceId = acceptance.AcceptanceId,
                    DonorUserId = acceptance.DonorUserId,
                    FulfilledAt = now
                });
            }

            request.FulfilledUnits += packets.Count;
            request.UpdatedAt = now;
            await CompleteIfFulfilledAsync(request);
            await NotifyExternalCreatorOfDonationProgressAsync(request, createdByHospital);

            var note = string.IsNullOrWhiteSpace(notes) ? string.Empty : $" Notes: {notes.Trim()}";
            await _context.Notifications.AddAsync(NotificationFactory.ForHospital(acceptance.DonorHospitalId!.Value, "HospitalDonationApproved",
                "Donation Approved",
                $"Dr. {doctor.FirstName} {doctor.LastName} approved your donation of {packets.Count} packet(s) to blood request #{shortId}.{note}"));

            await ActivityLogger.AddAsync(_context, doctorUserId, "Donation.HospitalApproved", ActivityLogger.Types.Donation, acceptance.AcceptanceId,
                $"Approved a hospital donation of {packets.Count} packet(s) for blood request #{NotificationFactory.ShortId(request.BloodRequestId)}.", doctor.HospitalId);
            await InventoryLedger.SavePacketChangesAsync(_context);
            return (await MapHospitalDonationsAsync(new List<Acceptance> { acceptance })).Single();
        }

        /// <summary>The assigned doctor rejects a hospital donation with a reason; the packets return to that hospital.</summary>
        public async Task<AcceptanceResponseDto> RejectHospitalDonationAsync(Guid acceptanceId, Guid doctorUserId, string? reason)
        {
            var message = RequireReason(reason, "A reason is required to reject a hospital donation.");
            var (acceptance, request, _) = await GetPendingHospitalDonationForDoctorAsync(acceptanceId, doctorUserId);

            await AcceptanceClosure.CloseAsync(_context, acceptance, request, AcceptanceStatus.Rejected, message, message);
            await _context.Notifications.AddAsync(NotificationFactory.ForHospital(acceptance.DonorHospitalId!.Value, "HospitalDonationRejected",
                "Donation Not Approved",
                $"Your donation to blood request #{NotificationFactory.ShortId(request.BloodRequestId)} was not approved. Reason: {message}. The packets are back in your inventory."));

            await ActivityLogger.AddAsync(_context, doctorUserId, "Donation.HospitalRejected", ActivityLogger.Types.Donation, acceptance.AcceptanceId,
                $"Rejected a hospital donation for blood request #{NotificationFactory.ShortId(request.BloodRequestId)}: {message}", request.HospitalId);
            await InventoryLedger.SavePacketChangesAsync(_context);
            return (await MapHospitalDonationsAsync(new List<Acceptance> { acceptance })).Single();
        }

        /// <summary>A hospital donation still waiting, and the doctor allowed to decide it (assigned, or fallback).</summary>
        private async Task<(Acceptance Acceptance, BloodRequest Request, Doctor Doctor)> GetPendingHospitalDonationForDoctorAsync(Guid acceptanceId, Guid doctorUserId)
        {
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == doctorUserId);
            if (doctor == null || !doctor.IsActive)
            {
                throw new UnauthorizedAccessException("Only active doctor accounts can decide on hospital donations.");
            }
            if (!DoctorAssignmentRules.HasCompletedFirstLogin(doctor))
            {
                throw new UnauthorizedAccessException("Sign in and change your temporary password before deciding on hospital donations.");
            }

            var acceptance = await _context.Acceptances.FindAsync(acceptanceId)
                             ?? throw new KeyNotFoundException($"Acceptance with ID {acceptanceId} was not found.");
            if (acceptance.DonorHospitalId == null)
            {
                throw new InvalidOperationException("This is a donor acceptance; review it in the screening queue.");
            }

            var request = await RequireRequestAsync(acceptance.BloodRequestId);
            SuspensionGuard.EnsureNotSuspended(request);

            var assignedDoctorId = await _context.BloodRequestVerifications
                .Where(v => v.BloodRequestId == request.BloodRequestId && v.Status != VerificationStatus.Closed)
                .OrderByDescending(v => v.UpdatedAt)
                .Select(v => v.DoctorId)
                .FirstOrDefaultAsync();
            var assignedDoctor = assignedDoctorId.HasValue ? await _context.Doctors.FindAsync(assignedDoctorId.Value) : null;

            if (assignedDoctor != null && assignedDoctor.IsActive)
            {
                if (assignedDoctor.DoctorId != doctor.DoctorId)
                {
                    throw new UnauthorizedAccessException("Only the doctor assigned to this blood request can decide on hospital donations.");
                }
            }
            else if (doctor.HospitalId != request.HospitalId)
            {
                // Assigned doctor removed: any active doctor of the requesting hospital may decide
                throw new UnauthorizedAccessException("Only doctors of the hospital handling this request can decide on hospital donations.");
            }

            if (acceptance.Status != AcceptanceStatus.Accepted)
            {
                throw new InvalidOperationException($"This hospital donation has already been decided ({acceptance.Status}).");
            }

            return (acceptance, request, doctor);
        }

        /// <summary>Packets each hospital acceptance held (found through its RESERVED audit rows, so they stay listed after approval).</summary>
        private async Task<Dictionary<Guid, List<DonatedPacketDto>>> PacketsByAcceptanceAsync(List<Guid> acceptanceIds)
        {
            if (acceptanceIds.Count == 0) return new Dictionary<Guid, List<DonatedPacketDto>>();

            var rows = await _context.InventoryTransactions
                .Where(t => t.ReferenceId != null && acceptanceIds.Contains(t.ReferenceId.Value) &&
                            t.TransactionType == TransactionType.Reserved && t.PacketId != null)
                .Select(t => new { AcceptanceId = t.ReferenceId!.Value, PacketId = t.PacketId!.Value })
                .ToListAsync();
            var packetIds = rows.Select(r => r.PacketId).Distinct().ToList();
            var packets = await _context.BloodPackets.Where(p => packetIds.Contains(p.PacketId)).ToDictionaryAsync(p => p.PacketId);

            return rows
                .Where(r => packets.ContainsKey(r.PacketId))
                .GroupBy(r => r.AcceptanceId)
                .ToDictionary(g => g.Key, g => g.Select(r => packets[r.PacketId]).DistinctBy(p => p.PacketId).OrderBy(p => p.TrackingNumber)
                    .Select(p => new DonatedPacketDto
                    {
                        PacketId = p.PacketId,
                        TrackingNumber = p.TrackingNumber,
                        BloodGroup = p.BloodGroup,
                        CollectionDate = p.CollectionDate,
                        ExpiryDate = p.ExpiryDate,
                        Status = p.Status
                    }).ToList());
        }

        private async Task<List<AcceptanceResponseDto>> MapHospitalDonationsAsync(List<Acceptance> acceptances)
        {
            if (acceptances.Count == 0) return new List<AcceptanceResponseDto>();

            var requestIds = acceptances.Select(a => a.BloodRequestId).Distinct().ToList();
            var requests = await _context.BloodRequests.Where(r => requestIds.Contains(r.BloodRequestId)).ToDictionaryAsync(r => r.BloodRequestId);
            var hospitalIds = requests.Values.Select(r => r.HospitalId)
                .Concat(acceptances.Where(a => a.DonorHospitalId != null).Select(a => a.DonorHospitalId!.Value))
                .Distinct().ToList();
            var hospitalNames = await _context.Hospitals.Where(h => hospitalIds.Contains(h.HospitalId)).ToDictionaryAsync(h => h.HospitalId, h => h.Name);
            var packets = await PacketsByAcceptanceAsync(acceptances.Select(a => a.AcceptanceId).ToList());

            return acceptances.Select(a =>
            {
                var dto = MapToResponseDto(a);
                dto.DonorHospitalName = a.DonorHospitalId.HasValue ? hospitalNames.GetValueOrDefault(a.DonorHospitalId.Value) : null;
                dto.Packets = packets.GetValueOrDefault(a.AcceptanceId) ?? new List<DonatedPacketDto>();
                if (requests.TryGetValue(a.BloodRequestId, out var r))
                {
                    AddRequestContext(dto, r, hospitalNames);
                }
                return dto;
            }).ToList();
        }

        // The request's details shown with an acceptance. A deleted request shows only that it was deleted: the
        // acceptance stays (closed) in the donor's or donating hospital's history, but the request is not shown.
        private static void AddRequestContext(AcceptanceResponseDto dto, BloodRequest r, IReadOnlyDictionary<Guid, string> hospitalNames)
        {
            dto.RequestStatus = r.Status.ToString();
            dto.RequestSuspended = r.AdminSuspendedAt != null;
            if (r.Status == BloodRequestStatus.Deleted)
            {
                dto.RequestDeleted = true;
                return;
            }

            dto.HospitalId = r.HospitalId;
            dto.HospitalName = hospitalNames.GetValueOrDefault(r.HospitalId);
            dto.RequestBloodGroup = r.BloodGroup;
            dto.RequestPriority = r.Priority;
            dto.UnitsRequired = r.UnitsRequired;
            dto.FulfilledUnits = r.FulfilledUnits;
            dto.ReservedUnits = r.ReservedUnits;
        }

        /// <summary>
        /// Screening transitions the Request Management agent may make: opening the interview only.
        /// Completing screening goes through SubmitScreeningReportAsync; decisions belong to doctors.
        /// </summary>
        public async Task<AcceptanceResponseDto> UpdateScreeningStatusAsync(Guid acceptanceId, AcceptanceStatus newStatus)
        {
            var acceptance = await _context.Acceptances.FindAsync(acceptanceId);
            if (acceptance == null)
            {
                throw new KeyNotFoundException($"Acceptance with ID {acceptanceId} was not found.");
            }

            if (acceptance.DonorHospitalId != null)
            {
                throw new InvalidOperationException("Hospital donations are approved by the assigned doctor and are not screened.");
            }
            await SuspensionGuard.EnsureRequestNotSuspendedAsync(_context, acceptance.BloodRequestId);

            // The agent retrying the same call (for example after a timeout) gets the current state, not an error
            if (acceptance.Status == AcceptanceStatus.ScreeningPending && newStatus == AcceptanceStatus.ScreeningPending)
            {
                return MapToResponseDto(acceptance);
            }

            if (!(acceptance.Status == AcceptanceStatus.Accepted && newStatus == AcceptanceStatus.ScreeningPending))
            {
                throw new InvalidOperationException($"Invalid status transition from {acceptance.Status} to {newStatus}.");
            }

            acceptance.Status = newStatus;
            await _context.SaveChangesAsync();

            return MapToResponseDto(acceptance);
        }

        /// <summary>
        /// Stores a submitted screening report as a new immutable version and routes it to the assigned doctor
        /// (any active doctor of the hospital may act as fallback).
        /// </summary>
        public async Task<DonorVerification> SubmitScreeningReportAsync(ScreeningReportNotificationDto dto)
        {
            if (!Guid.TryParse(dto.AcceptanceId, out var acceptanceId))
            {
                throw new ArgumentException("Invalid acceptance ID.");
            }

            var reportJson = dto.ReportJson?.Trim();
            if (string.IsNullOrWhiteSpace(reportJson))
            {
                throw new ArgumentException("The screening report content is required.");
            }
            if (reportJson.Length > MaxReportJsonLength)
            {
                throw new ArgumentException("The screening report is too large.");
            }
            try
            {
                using var _ = JsonDocument.Parse(reportJson);
            }
            catch (JsonException)
            {
                throw new ArgumentException("The screening report must be valid JSON.");
            }

            var acceptance = await _context.Acceptances.FindAsync(acceptanceId)
                             ?? throw new KeyNotFoundException($"Acceptance with ID {acceptanceId} was not found.");
            if (acceptance.DonorHospitalId != null)
            {
                throw new InvalidOperationException("Hospital donations are approved by the assigned doctor and are not screened.");
            }

            // The agent re-sending the same report (a retry after a timeout) gets the stored version, not a new one
            if (acceptance.Status == AcceptanceStatus.ScreeningCompleted)
            {
                var pending = await _context.DonorVerifications
                    .Where(v => v.AcceptanceId == acceptanceId)
                    .OrderByDescending(v => v.ReportVersion)
                    .FirstOrDefaultAsync();
                if (pending != null && pending.Status == VerificationStatus.Pending && pending.ReportJson == reportJson)
                {
                    return pending;
                }
            }

            if (acceptance.Status != AcceptanceStatus.Accepted && acceptance.Status != AcceptanceStatus.ScreeningPending)
            {
                throw new InvalidOperationException($"A screening report cannot be submitted while the acceptance is {acceptance.Status}.");
            }

            await DonorEligibility.RequireEligibleDonorAccountAsync(_context, acceptance.DonorUserId);

            var request = await RequireRequestAsync(acceptance.BloodRequestId);
            SuspensionGuard.EnsureNotSuspended(request);
            if (request.Status != BloodRequestStatus.Approved)
            {
                throw new InvalidOperationException($"The blood request is {request.Status}, so the report cannot be submitted.");
            }

            var latestVersion = await _context.DonorVerifications
                .Where(v => v.AcceptanceId == acceptanceId)
                .MaxAsync(v => (int?)v.ReportVersion) ?? 0;

            var assignedDoctorId = await _context.BloodRequestVerifications
                .Where(v => v.BloodRequestId == request.BloodRequestId && v.DoctorId != null && v.Status != VerificationStatus.Closed)
                .OrderByDescending(v => v.UpdatedAt)
                .Select(v => v.DoctorId)
                .FirstOrDefaultAsync();

            var now = DateTime.UtcNow;
            var report = new DonorVerification
            {
                DonorVerificationId = Guid.NewGuid(),
                AcceptanceId = acceptanceId,
                DoctorId = assignedDoctorId,
                Status = VerificationStatus.Pending,
                ReportVersion = latestVersion + 1,
                ReportJson = reportJson,
                MedicalReportSummary = string.IsNullOrWhiteSpace(dto.Summary) ? null : dto.Summary.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };
            await _context.DonorVerifications.AddAsync(report);
            acceptance.Status = AcceptanceStatus.ScreeningCompleted;

            await NotifyEligibleDoctorOrHospitalAsync(request, assignedDoctorId, "ScreeningReportSubmitted", "Donor Screening Report Ready",
                $"A donor screening report (version {report.ReportVersion}) for blood request #{NotificationFactory.ShortId(request.BloodRequestId)} ({request.BloodGroup}) is waiting for your review.");

            await ActivityLogger.AddAsync(_context, acceptance.DonorUserId, "Screening.Submitted", ActivityLogger.Types.Screening, report.DonorVerificationId,
                $"Submitted screening answers (report version {report.ReportVersion}) for blood request #{NotificationFactory.ShortId(request.BloodRequestId)}.");
            await _context.SaveChangesAsync();
            return report;
        }

        /// <summary>
        /// Saves a new acceptance. If its request was deleted at the same moment, the foreign key rejects the insert:
        /// that becomes a clear 409 instead of a server error. Concurrency conflicts keep their own handling.
        /// </summary>
        private static async Task SaveNewAcceptanceAsync(Func<Task> save)
        {
            try
            {
                await save();
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
            {
                throw new ConflictException(DatabaseConflicts.ConflictMessage(ex) ?? "This blood request is no longer available (it was just deleted).");
            }
        }

        private async Task<BloodRequest> RequireRequestAsync(Guid requestId) =>
            await _context.BloodRequests.FindAsync(requestId)
            ?? throw new KeyNotFoundException($"Blood request with ID {requestId} was not found.");

        /// <summary>Active doctor of the request's hospital, or that hospital's staff account.</summary>
        private async Task RequireHospitalAuthorityAsync(BloodRequest request, Guid actorUserId, Guid? actingHospitalId)
        {
            if (actingHospitalId.HasValue)
            {
                if (actingHospitalId.Value != request.HospitalId)
                {
                    throw new UnauthorizedAccessException("Only staff of the hospital handling this request can do this.");
                }
                return;
            }

            // Doctors are resolved from their login; tests may pass the DoctorId directly
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == actorUserId || d.DoctorId == actorUserId);
            if (doctor == null || !doctor.IsActive)
            {
                throw new UnauthorizedAccessException("Only verified doctors assigned to this hospital can finalize donor selection.");
            }

            var doctorHospital = await _context.Hospitals.FindAsync(doctor.HospitalId);
            if (doctorHospital == null || !doctorHospital.IsVerified || doctor.HospitalId != request.HospitalId)
            {
                throw new UnauthorizedAccessException("Only verified doctors assigned to this hospital can finalize donor selection.");
            }
        }

        private async Task NotifyAssignedDoctorOrHospitalAsync(BloodRequest request, Guid? doctorId, string type, string title, string message)
        {
            var doctorUserId = await NotificationFactory.DoctorUserIdAsync(_context, doctorId);
            await _context.Notifications.AddAsync(doctorUserId.HasValue
                ? NotificationFactory.ForUser(doctorUserId.Value, "Doctor", type, title, message)
                : NotificationFactory.ForHospital(request.HospitalId, type, title, message));
        }

        private async Task NotifyEligibleDoctorOrHospitalAsync(BloodRequest request, Guid? doctorId, string type, string title, string message)
        {
            var doctorUserId = await NotificationFactory.EligibleDoctorUserIdAsync(_context, doctorId, request.HospitalId);
            await _context.Notifications.AddAsync(doctorUserId.HasValue
                ? NotificationFactory.ForUser(doctorUserId.Value, "Doctor", type, title, message)
                : NotificationFactory.ForHospital(request.HospitalId, type, title, message));
        }

        private async Task NotifyExternalCreatorOfDonationProgressAsync(BloodRequest request, bool createdByHospital)
        {
            if (createdByHospital) return;

            var completed = request.Status == BloodRequestStatus.Completed;
            await _context.Notifications.AddAsync(NotificationFactory.ForUser(request.PatientUserId, "User",
                completed ? "BloodRequestCompleted" : "DonationProgress",
                completed ? "Blood Request Fulfilled" : "Blood Request Donation Progress",
                completed
                    ? $"Your blood request #{NotificationFactory.ShortId(request.BloodRequestId)} has been fulfilled ({request.FulfilledUnits}/{request.UnitsRequired} unit(s))."
                    : $"A successful donation was recorded for your blood request #{NotificationFactory.ShortId(request.BloodRequestId)}. Progress: {request.FulfilledUnits}/{request.UnitsRequired} unit(s)."));
        }

        private async Task NotifyRequestStaffAsync(BloodRequest request, string type, string title, string message)
        {
            var assignedDoctorId = await _context.BloodRequestVerifications
                .Where(v => v.BloodRequestId == request.BloodRequestId && v.DoctorId != null && v.Status != VerificationStatus.Closed)
                .OrderByDescending(v => v.UpdatedAt)
                .Select(v => v.DoctorId)
                .FirstOrDefaultAsync();
            await NotifyAssignedDoctorOrHospitalAsync(request, assignedDoctorId, type, title, message);
        }

        /// <summary>
        /// A donor withdrawal affects both operational scopes: the request hospital always receives an in-app notice,
        /// and the latest assigned doctor receives one only while their doctor and login accounts remain active.
        /// </summary>
        private async Task NotifyWithdrawalRecipientsAsync(BloodRequest request, string type, string title, string message)
        {
            await _context.Notifications.AddAsync(NotificationFactory.ForHospital(request.HospitalId, type, title, message));

            var assignedDoctorId = await _context.BloodRequestVerifications
                .Where(v => v.BloodRequestId == request.BloodRequestId && v.DoctorId != null && v.Status != VerificationStatus.Closed)
                .OrderByDescending(v => v.UpdatedAt)
                .Select(v => v.DoctorId)
                .FirstOrDefaultAsync();
            if (!assignedDoctorId.HasValue) return;

            var doctorUserId = await _context.Doctors
                .Where(d => d.DoctorId == assignedDoctorId.Value && d.HospitalId == request.HospitalId && d.IsActive &&
                            !d.MustChangePassword && d.DeletedAt == null && d.UserId != null &&
                            _context.Users.Any(u => u.UserId == d.UserId.Value && u.AccountStatus == AccountStatus.Active &&
                                                    !u.IsSuspended && !u.IsPermanentlyBlocked))
                .Select(d => d.UserId)
                .FirstOrDefaultAsync();
            if (doctorUserId.HasValue)
            {
                await _context.Notifications.AddAsync(NotificationFactory.ForUser(doctorUserId.Value, "Doctor", type, title, message));
            }
        }

        private static string RequireReason(string? reason, string missingMessage)
        {
            var message = reason?.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new InvalidOperationException(missingMessage);
            }
            if (message.Length > 500)
            {
                throw new InvalidOperationException("The reason cannot exceed 500 characters.");
            }
            return message;
        }

        private static AcceptanceResponseDto MapToResponseDto(Acceptance acceptance)
        {
            return new AcceptanceResponseDto
            {
                AcceptanceId = acceptance.AcceptanceId,
                BloodRequestId = acceptance.BloodRequestId,
                DonorUserId = acceptance.DonorUserId,
                Status = acceptance.Status.ToString(),
                AcceptedAt = acceptance.AcceptedAt,
                CancelledAt = acceptance.CancelledAt,
                RejectionReason = acceptance.RejectionReason,
                DonorHospitalId = acceptance.DonorHospitalId
            };
        }
    }
}
