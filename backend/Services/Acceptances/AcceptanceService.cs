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
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Acceptances
{
    /// <summary>
    /// Donor side of a blood request: accept → AI screening (report versions) → doctor approval reserves a slot
    /// → recorded donation fills it. Withdrawal or release frees a reserved slot. FulfilledUnits counts recorded
    /// donations only; ReservedUnits counts approved donors who have not donated yet.
    /// </summary>
    public class AcceptanceService : IAcceptanceService
    {
        public const string FulfilledByOthersReason = "Required donor count has been fulfilled by other selected donors.";
        private const int MaxReportJsonLength = 200_000;

        private readonly AppDbContext _context;
        private readonly IBloodCompatibilityService _bloodCompatibilityService;
        private readonly IPlanningAgentService? _planningAgent;

        public AcceptanceService(
            AppDbContext context,
            IBloodCompatibilityService bloodCompatibilityService,
            IPlanningAgentService? planningAgent = null)
        {
            _context = context;
            _bloodCompatibilityService = bloodCompatibilityService;
            _planningAgent = planningAgent;
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

            if (request.ExpiryDate <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("Cannot accept an expired blood request.");
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
                (a.Status == AcceptanceStatus.ScreeningPending ||
                 a.Status == AcceptanceStatus.Verified ||
                 a.Status == AcceptanceStatus.ScreeningCompleted));
            if (hasActiveProcess)
            {
                throw new InvalidOperationException("You already have an active donation process.");
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
            await _context.SaveChangesAsync();

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
            var hadReport = acceptance.Status == AcceptanceStatus.ScreeningCompleted || wasReserved;

            await AcceptanceClosure.CloseAsync(_context, acceptance, request, AcceptanceStatus.Cancelled, null, "Donor withdrew.");

            if (hadReport)
            {
                await NotifyRequestStaffAsync(request, "DonorWithdrew", "Donor Withdrew",
                    $"A donor withdrew from blood request #{NotificationFactory.ShortId(request.BloodRequestId)} ({request.BloodGroup})." +
                    (wasReserved ? " Their reserved slot is free again." : string.Empty));
            }

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
            await RequireHospitalAuthorityAsync(request, actorUserId, actingHospitalId);

            if (!AcceptanceClosure.IsActive(acceptance.Status))
            {
                throw new InvalidOperationException($"Only active acceptances can be released. This one is {acceptance.Status}.");
            }

            await AcceptanceClosure.CloseAsync(_context, acceptance, request, AcceptanceStatus.Cancelled,
                $"Released by the hospital: {message}", $"Released by the hospital: {message}");

            await _context.Notifications.AddAsync(NotificationFactory.ForUser(acceptance.DonorUserId, "Donor", "DonationReservationReleased",
                "Donation Reservation Released",
                $"Your donation for blood request #{NotificationFactory.ShortId(request.BloodRequestId)} was released by the hospital. Reason: {message}"));

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
            await NotifyAssignedDoctorOrHospitalAsync(request, latest.DoctorId, "ScreeningReportSuperseded", "Screening Report Being Updated",
                $"The donor is updating their screening answers for request #{NotificationFactory.ShortId(request.BloodRequestId)}. A new report version will follow.");

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
                    dto.HospitalId = r.HospitalId;
                    dto.HospitalName = hospitalNames.GetValueOrDefault(r.HospitalId);
                    dto.RequestBloodGroup = r.BloodGroup;
                    dto.RequestPriority = r.Priority;
                    dto.RequestStatus = r.Status.ToString();
                    dto.UnitsRequired = r.UnitsRequired;
                    dto.FulfilledUnits = r.FulfilledUnits;
                    dto.ReservedUnits = r.ReservedUnits;
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
                ? (v.DecidedByDoctor != null ? $"Dr. {v.DecidedByDoctor.FirstName} {v.DecidedByDoctor.LastName}".Trim() : "Removed doctor")
                : null,
            ApprovalNotes = v.Status == VerificationStatus.Approved ? v.Notes : null,
            RejectionReason = v.Status == VerificationStatus.Rejected ? v.Notes : null,
            Note = v.Status is VerificationStatus.Superseded or VerificationStatus.Closed ? v.Notes : null
        };

        public async Task<AcceptanceResponseDto?> GetAcceptanceByIdAsync(Guid acceptanceId)
        {
            var acceptance = await _context.Acceptances.FindAsync(acceptanceId);
            return acceptance != null ? MapToResponseDto(acceptance) : null;
        }

        public async Task<List<RequestAcceptanceDetailDto>> GetRequestAcceptancesAsync(Guid bloodRequestId)
        {
            var acceptances = await _context.Acceptances
                .Where(a => a.BloodRequestId == bloodRequestId)
                .OrderBy(a => a.AcceptedAt)
                .ToListAsync();

            var result = new List<RequestAcceptanceDetailDto>();
            foreach (var a in acceptances)
            {
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

            var closedCount = 0;
            if (request.FulfilledUnits >= request.UnitsRequired)
            {
                request.Status = BloodRequestStatus.Completed;
                var closed = await AcceptanceClosure.CloseAllAsync(_context, request, AcceptanceClosure.InScreeningStatuses,
                    AcceptanceStatus.Rejected, FulfilledByOthersReason, "Request fulfilled before a decision was needed.");
                closedCount = closed.Count;
                foreach (var a in closed)
                {
                    await _context.Notifications.AddAsync(NotificationFactory.ForUser(a.DonorUserId, "Donor", "RequestFulfilled",
                        "Blood Request Fulfilled",
                        $"Blood request #{NotificationFactory.ShortId(bloodRequestId)} has been fulfilled by other donors. Thank you for offering to help."));
                }
            }

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
            if (acceptance.Status != AcceptanceStatus.Accepted && acceptance.Status != AcceptanceStatus.ScreeningPending)
            {
                throw new InvalidOperationException($"A screening report cannot be submitted while the acceptance is {acceptance.Status}.");
            }

            await DonorEligibility.RequireEligibleDonorAccountAsync(_context, acceptance.DonorUserId);

            var request = await RequireRequestAsync(acceptance.BloodRequestId);
            if (request.Status != BloodRequestStatus.Approved)
            {
                throw new InvalidOperationException($"The blood request is {request.Status}, so the report cannot be submitted.");
            }

            var latestVersion = await _context.DonorVerifications
                .Where(v => v.AcceptanceId == acceptanceId)
                .MaxAsync(v => (int?)v.ReportVersion) ?? 0;

            var assignedDoctorId = await _context.BloodRequestVerifications
                .Where(v => v.BloodRequestId == request.BloodRequestId && v.DoctorId != null)
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

            await NotifyAssignedDoctorOrHospitalAsync(request, assignedDoctorId, "ScreeningReportSubmitted", "Donor Screening Report Ready",
                $"A donor screening report (version {report.ReportVersion}) for blood request #{NotificationFactory.ShortId(request.BloodRequestId)} ({request.BloodGroup}) is waiting for your review.");

            await _context.SaveChangesAsync();
            return report;
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

        private async Task NotifyRequestStaffAsync(BloodRequest request, string type, string title, string message)
        {
            var assignedDoctorId = await _context.BloodRequestVerifications
                .Where(v => v.BloodRequestId == request.BloodRequestId && v.DoctorId != null)
                .OrderByDescending(v => v.UpdatedAt)
                .Select(v => v.DoctorId)
                .FirstOrDefaultAsync();
            await NotifyAssignedDoctorOrHospitalAsync(request, assignedDoctorId, type, title, message);
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
                RejectionReason = acceptance.RejectionReason
            };
        }
    }
}
