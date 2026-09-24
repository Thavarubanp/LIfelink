using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using LifeLink.Services.Common;
using LifeLink.Data;
using LifeLink.DTOs.Verification;
using LifeLink.Entities;
using LifeLink.DTOs.Planning;
using LifeLink.Services.Planning;
using LifeLink.Services.Notification;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Verification
{
    public class VerificationService : IVerificationService
    {
        private readonly AppDbContext _context;
        private readonly INotificationAgentService _notificationAgent;
        private readonly IPlanningAgentService? _planningAgent;

        public VerificationService(
            AppDbContext context,
            INotificationAgentService notificationAgent,
            IPlanningAgentService? planningAgent = null)
        {
            _context = context;
            _notificationAgent = notificationAgent;
            _planningAgent = planningAgent;
        }

        /// <summary>
        /// Hospital verifies a pending request it received and assigns one of its own doctors (mandatory).
        /// The request moves to Verified and waits for that doctor's decision.
        /// </summary>
        public async Task<BloodRequestVerificationResponseDto> VerifyBloodRequestAsync(Guid requestId, Guid hospitalId, Guid doctorId)
        {
            var request = await GetHospitalRequestAsync(requestId, hospitalId);
            if (request.Status != BloodRequestStatus.Pending)
            {
                throw new InvalidOperationException($"Only pending requests can be verified. This request is {request.Status}.");
            }

            if (doctorId == Guid.Empty)
            {
                throw new InvalidOperationException("A doctor must be assigned before the request can be verified.");
            }

            var doctor = await _context.Doctors.FindAsync(doctorId);
            if (doctor == null || doctor.HospitalId != hospitalId)
            {
                throw new InvalidOperationException("The selected doctor does not belong to your hospital.");
            }

            if (!doctor.IsActive || doctor.UserId == null)
            {
                throw new InvalidOperationException("The selected doctor does not have an active account.");
            }

            var now = DateTime.UtcNow;
            var verification = await _context.BloodRequestVerifications
                .FirstOrDefaultAsync(v => v.BloodRequestId == requestId);

            if (verification == null)
            {
                verification = new BloodRequestVerification
                {
                    VerificationId = Guid.NewGuid(),
                    BloodRequestId = requestId,
                    CreatedAt = now
                };
                await _context.BloodRequestVerifications.AddAsync(verification);
            }

            verification.DoctorId = doctor.DoctorId;
            verification.Status = VerificationStatus.Pending;
            verification.Notes = null;
            verification.VerifiedAt = null;
            verification.UpdatedAt = now;

            request.Status = BloodRequestStatus.Verified;
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();

            return MapVerification(verification, doctor);
        }

        /// <summary>
        /// Hospital rejects a request it received. No doctor is required; a reason is.
        /// </summary>
        public async Task RejectBloodRequestByHospitalAsync(Guid requestId, Guid hospitalId, string? reason)
        {
            var message = RequireReason(reason);
            var request = await GetHospitalRequestAsync(requestId, hospitalId);

            if (request.Status != BloodRequestStatus.Pending && request.Status != BloodRequestStatus.Verified)
            {
                throw new InvalidOperationException($"Only pending or verified requests can be rejected. This request is {request.Status}.");
            }

            var now = DateTime.UtcNow;

            // A doctor assignment still awaiting a decision is closed by the hospital's rejection
            var pendingAssignment = await _context.BloodRequestVerifications
                .FirstOrDefaultAsync(v => v.BloodRequestId == requestId && v.Status == VerificationStatus.Pending);
            if (pendingAssignment != null)
            {
                pendingAssignment.Status = VerificationStatus.Rejected;
                pendingAssignment.Notes = $"Rejected by hospital: {message}";
                pendingAssignment.VerifiedAt = now;
                pendingAssignment.UpdatedAt = now;
            }

            request.Status = BloodRequestStatus.Rejected;
            request.RejectionReason = message;
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Assigned doctor approves a verified request. The doctor is resolved from the caller's account,
        /// never from the request body. Approved requests become visible to donors.
        /// </summary>
        public async Task<BloodRequestVerificationResponseDto> ApproveBloodRequestAsync(Guid requestId, Guid doctorUserId, string? notes)
        {
            var (request, verification, doctor) = await GetAssignmentForDoctorAsync(requestId, doctorUserId);

            var now = DateTime.UtcNow;
            verification.Status = VerificationStatus.Approved;
            verification.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            verification.VerifiedAt = now;
            verification.UpdatedAt = now;

            request.Status = BloodRequestStatus.Approved;
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();

            await DispatchApprovalAlertsAsync(request);

            return MapVerification(verification, doctor);
        }

        /// <summary>
        /// Workflow A (BloodRequestApproved): the Supervisor routes to the Notification agent with the donors and
        /// hospitals the backend selected; returned alerts are saved only for those recipients. If the Supervisor is
        /// unreachable, the direct Notification agent path (with its deterministic fallback) runs instead.
        /// </summary>
        private async Task DispatchApprovalAlertsAsync(BloodRequest request)
        {
            var priority = !string.IsNullOrWhiteSpace(request.Priority) ? request.Priority : "Normal";
            var isUrgent = priority.Equals("High", StringComparison.OrdinalIgnoreCase) || priority.Equals("Critical", StringComparison.OrdinalIgnoreCase);

            if (_planningAgent != null)
            {
                var candidates = await _notificationAgent.GetEligibleDonorCandidatesAsync(request.BloodRequestId, request.BloodGroup, request.PatientUserId);
                var hospitalIds = isUrgent ? await _notificationAgent.GetAlertHospitalIdsAsync(request.HospitalId) : new List<Guid>();
                var hospitalName = await _context.Hospitals.Where(h => h.HospitalId == request.HospitalId).Select(h => h.Name).FirstOrDefaultAsync() ?? "Partner Hospital";

                var planResult = await _planningAgent.DispatchPlanAsync(new PlanRequestDto
                {
                    EventType = "BloodRequestApproved",
                    RequestId = request.BloodRequestId.ToString(),
                    BloodGroup = request.BloodGroup,
                    Urgency = priority,
                    HospitalId = request.HospitalId.ToString(),
                    UnitsRequired = request.UnitsRequired,
                    Payload = new Dictionary<string, object>
                    {
                        ["requestId"] = request.BloodRequestId.ToString(),
                        ["bloodGroup"] = request.BloodGroup,
                        ["hospitalId"] = request.HospitalId.ToString(),
                        ["hospitalName"] = hospitalName,
                        ["priority"] = priority,
                        ["unitsRequired"] = request.UnitsRequired - request.FulfilledUnits,
                        ["availableDonors"] = candidates.Select(c => c.ToAgentPayload()).ToList(),
                        ["verifiedHospitalIds"] = hospitalIds.Select(id => id.ToString()).ToList()
                    }
                });

                if (planResult != null && planResult.Success)
                {
                    await _notificationAgent.PersistAgentNotificationsAsync(planResult.Notifications,
                        candidates.Select(c => c.UserId).ToHashSet(), hospitalIds.ToHashSet());
                    return;
                }
            }

            await _notificationAgent.ProcessRequestApprovalNotificationAsync(request.BloodRequestId, request.BloodGroup, request.HospitalId, priority);
        }

        /// <summary>
        /// Assigned doctor rejects a verified request with a reason shown to the creator.
        /// </summary>
        public async Task<BloodRequestVerificationResponseDto> RejectBloodRequestAsync(Guid requestId, Guid doctorUserId, string? reason)
        {
            var message = RequireReason(reason);
            var (request, verification, doctor) = await GetAssignmentForDoctorAsync(requestId, doctorUserId);

            var now = DateTime.UtcNow;
            verification.Status = VerificationStatus.Rejected;
            verification.Notes = message;
            verification.VerifiedAt = now;
            verification.UpdatedAt = now;

            request.Status = BloodRequestStatus.Rejected;
            request.RejectionReason = message;
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();

            return MapVerification(verification, doctor);
        }

        private async Task<BloodRequest> GetHospitalRequestAsync(Guid requestId, Guid hospitalId)
        {
            var request = await _context.BloodRequests.FindAsync(requestId);
            if (request == null)
            {
                throw new KeyNotFoundException($"Blood request with ID {requestId} was not found.");
            }

            if (request.HospitalId != hospitalId)
            {
                throw new UnauthorizedAccessException("This blood request was not sent to your hospital.");
            }

            return request;
        }

        private async Task<(BloodRequest Request, BloodRequestVerification Verification, Doctor Doctor)> GetAssignmentForDoctorAsync(Guid requestId, Guid doctorUserId)
        {
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == doctorUserId);
            if (doctor == null)
            {
                throw new UnauthorizedAccessException("Only doctor accounts can review blood requests.");
            }

            var request = await _context.BloodRequests.FindAsync(requestId);
            if (request == null)
            {
                throw new KeyNotFoundException($"Blood request with ID {requestId} was not found.");
            }

            var verification = await _context.BloodRequestVerifications
                .FirstOrDefaultAsync(v => v.BloodRequestId == requestId && v.DoctorId == doctor.DoctorId);
            if (verification == null)
            {
                throw new UnauthorizedAccessException("This blood request is not assigned to you.");
            }

            if (request.Status != BloodRequestStatus.Verified || verification.Status != VerificationStatus.Pending)
            {
                throw new InvalidOperationException($"This request has already been decided ({request.Status}).");
            }

            return (request, verification, doctor);
        }

        private static string RequireReason(string? reason)
        {
            var message = reason?.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new InvalidOperationException("A rejection message is required.");
            }

            if (message.Length > 500)
            {
                throw new InvalidOperationException("The rejection message cannot exceed 500 characters.");
            }

            return message;
        }

        private static BloodRequestVerificationResponseDto MapVerification(BloodRequestVerification verification, Doctor? doctor)
        {
            return new BloodRequestVerificationResponseDto
            {
                VerificationId = verification.VerificationId,
                BloodRequestId = verification.BloodRequestId,
                DoctorId = verification.DoctorId,
                DoctorName = doctor != null ? $"{doctor.FirstName} {doctor.LastName}" : string.Empty,
                Status = verification.Status.ToString(),
                Notes = verification.Notes,
                VerifiedAt = verification.VerifiedAt,
                CreatedAt = verification.CreatedAt
            };
        }

        /// <summary>
        /// Doctor approves a screening report version. Approval reserves one donation slot (ReservedUnits); it does
        /// not count as fulfilled until the donation is recorded. The assigned doctor is the primary reviewer; any
        /// active doctor of the request's hospital may act as fallback. AI agents can never call this.
        /// </summary>
        public async Task<DonorVerificationResponseDto> ApproveDonorVerificationAsync(Guid id, Guid doctorUserId, string? notes)
        {
            var (report, acceptance, request, doctor) = await GetPendingReportForDoctorAsync(id, doctorUserId);

            var donor = await _context.Users.FindAsync(acceptance.DonorUserId);
            if (donor == null || !DonorEligibility.IsActiveAccount(donor))
            {
                throw new InvalidOperationException("The donor's account is not active. Reject the report or release the donor instead.");
            }

            if (request.Status != BloodRequestStatus.Approved)
            {
                throw new InvalidOperationException($"The blood request is {request.Status}, so donors can no longer be approved.");
            }

            if (request.FulfilledUnits + request.ReservedUnits >= request.UnitsRequired)
            {
                throw new InvalidOperationException("No donation slot is free. The donor stays on standby until a reserved slot is released.");
            }

            var now = DateTime.UtcNow;
            report.Status = VerificationStatus.Approved;
            report.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            report.DecidedByDoctorId = doctor.DoctorId;
            report.VerifiedAt = now;
            report.UpdatedAt = now;

            acceptance.Status = AcceptanceStatus.Verified;
            request.ReservedUnits++;
            request.UpdatedAt = now;

            await _context.Notifications.AddAsync(NotificationFactory.ForUser(acceptance.DonorUserId, "Donor", "DonorApproved",
                "You Are Approved to Donate",
                $"Dr. {doctor.FirstName} {doctor.LastName} approved your screening for blood request #{NotificationFactory.ShortId(request.BloodRequestId)}. A donation slot is reserved for you." +
                (report.Notes != null ? $" Notes: {report.Notes}" : string.Empty)));

            await _context.SaveChangesAsync();
            return await MapDonorVerificationAsync(report, doctorUserId);
        }

        /// <summary>Doctor rejects a screening report version with a reason the donor can see. The request stays public.</summary>
        public async Task<DonorVerificationResponseDto> RejectDonorVerificationAsync(Guid id, Guid doctorUserId, string? reason)
        {
            var message = RequireReason(reason);
            var (report, acceptance, request, doctor) = await GetPendingReportForDoctorAsync(id, doctorUserId);

            var now = DateTime.UtcNow;
            report.Status = VerificationStatus.Rejected;
            report.Notes = message;
            report.DecidedByDoctorId = doctor.DoctorId;
            report.VerifiedAt = now;
            report.UpdatedAt = now;

            acceptance.Status = AcceptanceStatus.Rejected;
            acceptance.RejectionReason = message;

            await _context.Notifications.AddAsync(NotificationFactory.ForUser(acceptance.DonorUserId, "Donor", "DonorRejected",
                "Screening Not Approved",
                $"Your screening for blood request #{NotificationFactory.ShortId(request.BloodRequestId)} was not approved. Reason: {message}"));

            await _context.SaveChangesAsync();
            return await MapDonorVerificationAsync(report, doctorUserId);
        }

        private async Task<(DonorVerification Report, Acceptance Acceptance, BloodRequest Request, Doctor Doctor)> GetPendingReportForDoctorAsync(Guid id, Guid doctorUserId)
        {
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == doctorUserId);
            if (doctor == null || !doctor.IsActive)
            {
                throw new UnauthorizedAccessException("Only active doctor accounts can review donor screening reports.");
            }

            // Accepts a report id, or an acceptance id meaning its latest version
            var report = await _context.DonorVerifications.FirstOrDefaultAsync(v => v.DonorVerificationId == id)
                         ?? await _context.DonorVerifications.Where(v => v.AcceptanceId == id).OrderByDescending(v => v.ReportVersion).FirstOrDefaultAsync();
            if (report == null)
            {
                throw new KeyNotFoundException($"Screening report {id} was not found.");
            }

            var acceptance = await _context.Acceptances.FindAsync(report.AcceptanceId)
                             ?? throw new KeyNotFoundException("The donor acceptance for this report was not found.");
            var request = await _context.BloodRequests.FindAsync(acceptance.BloodRequestId)
                          ?? throw new KeyNotFoundException("The blood request for this report was not found.");

            if (doctor.HospitalId != request.HospitalId)
            {
                throw new UnauthorizedAccessException("Only doctors of the hospital handling this request can review its donors.");
            }

            if (report.Status != VerificationStatus.Pending)
            {
                throw new InvalidOperationException($"This report version is already {report.Status}.");
            }

            var hasNewerVersion = await _context.DonorVerifications.AnyAsync(v => v.AcceptanceId == report.AcceptanceId && v.ReportVersion > report.ReportVersion);
            if (hasNewerVersion || acceptance.Status != AcceptanceStatus.ScreeningCompleted)
            {
                throw new InvalidOperationException("This report version is no longer the one awaiting review.");
            }

            return (report, acceptance, request, doctor);
        }

        public async Task<List<BloodRequestVerificationResponseDto>> GetBloodRequestVerificationsAsync()
        {
            var list = await _context.BloodRequestVerifications
                .Include(v => v.Doctor)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();

            return list.Select(v => new BloodRequestVerificationResponseDto
            {
                VerificationId = v.VerificationId,
                BloodRequestId = v.BloodRequestId,
                DoctorId = v.DoctorId,
                DoctorName = v.Doctor != null ? $"{v.Doctor.FirstName} {v.Doctor.LastName}" : string.Empty,
                Status = v.Status.ToString(),
                Notes = v.Notes,
                VerifiedAt = v.VerifiedAt,
                CreatedAt = v.CreatedAt
            }).ToList();
        }

        /// <summary>
        /// Screening report versions with decisions. Doctors see their own hospital's reports (assigned ones flagged);
        /// the Admin (callerUserId null) sees all of them.
        /// </summary>
        public async Task<List<DonorVerificationResponseDto>> GetDonorVerificationsAsync(Guid? doctorUserId)
        {
            IQueryable<DonorVerification> query = _context.DonorVerifications
                .Include(v => v.Doctor)
                .Include(v => v.DecidedByDoctor);

            if (doctorUserId.HasValue)
            {
                var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == doctorUserId);
                if (doctor == null)
                {
                    throw new UnauthorizedAccessException("Only doctor accounts can view donor screening reports.");
                }
                var hospitalAcceptanceIds = _context.Acceptances
                    .Where(a => _context.BloodRequests.Any(r => r.BloodRequestId == a.BloodRequestId && r.HospitalId == doctor.HospitalId))
                    .Select(a => a.AcceptanceId);
                query = query.Where(v => hospitalAcceptanceIds.Contains(v.AcceptanceId));
            }

            var reports = await query.OrderByDescending(v => v.CreatedAt).ToListAsync();
            var result = new List<DonorVerificationResponseDto>();
            foreach (var report in reports)
            {
                result.Add(await MapDonorVerificationAsync(report, doctorUserId));
            }
            return result;
        }

        private async Task<DonorVerificationResponseDto> MapDonorVerificationAsync(DonorVerification v, Guid? viewerDoctorUserId)
        {
            var doctor = v.Doctor ?? (v.DoctorId.HasValue ? await _context.Doctors.FindAsync(v.DoctorId.Value) : null);
            var decidedBy = v.DecidedByDoctor ?? (v.DecidedByDoctorId.HasValue ? await _context.Doctors.FindAsync(v.DecidedByDoctorId.Value) : null);
            var acceptance = await _context.Acceptances.FindAsync(v.AcceptanceId);
            var request = acceptance != null ? await _context.BloodRequests.FindAsync(acceptance.BloodRequestId) : null;
            var donor = acceptance != null ? await _context.Users.FindAsync(acceptance.DonorUserId) : null;
            var latestVersion = await _context.DonorVerifications.Where(x => x.AcceptanceId == v.AcceptanceId).MaxAsync(x => (int?)x.ReportVersion) ?? v.ReportVersion;
            var (riskLevel, recommendation) = ReadRiskFromReport(v.ReportJson);
            var decided = v.Status is VerificationStatus.Approved or VerificationStatus.Rejected;

            return new DonorVerificationResponseDto
            {
                DonorVerificationId = v.DonorVerificationId,
                AcceptanceId = v.AcceptanceId,
                ReportVersion = v.ReportVersion,
                IsLatestVersion = v.ReportVersion == latestVersion,
                DoctorId = v.DoctorId,
                DoctorName = doctor != null ? $"{doctor.FirstName} {doctor.LastName}" : (v.DoctorId == null ? string.Empty : "Removed doctor"),
                DecidedByDoctorId = v.DecidedByDoctorId,
                DecidedByName = !decided ? null : (decidedBy != null ? $"{decidedBy.FirstName} {decidedBy.LastName}" : "Removed doctor"),
                IsAssignedToMe = viewerDoctorUserId.HasValue && doctor?.UserId == viewerDoctorUserId,
                Status = v.Status.ToString(),
                MedicalReportSummary = v.MedicalReportSummary,
                ReportJson = v.ReportJson,
                RiskLevel = riskLevel,
                Recommendation = recommendation,
                Notes = v.Notes,
                VerifiedAt = v.VerifiedAt,
                CreatedAt = v.CreatedAt,
                DonorUserId = acceptance?.DonorUserId,
                DonorName = donor == null ? null : (donor.AccountStatus == AccountStatus.Deleted ? AccountLifecycleHelper.DeletedUserName : $"{donor.FirstName} {donor.LastName}".Trim()),
                DonorBloodGroup = donor?.BloodGroup,
                DonorAccountStatus = donor == null ? null : (donor.IsSuspended ? "Suspended" : donor.AccountStatus.ToString()),
                AcceptanceStatus = acceptance?.Status.ToString(),
                BloodRequestId = request?.BloodRequestId,
                RequestBloodGroup = request?.BloodGroup,
                RequestStatus = request?.Status.ToString(),
                UnitsRequired = request?.UnitsRequired ?? 0,
                FulfilledUnits = request?.FulfilledUnits ?? 0,
                ReservedUnits = request?.ReservedUnits ?? 0,
                HasFreeSlot = request != null && request.Status == BloodRequestStatus.Approved &&
                              request.FulfilledUnits + request.ReservedUnits < request.UnitsRequired
            };
        }

        private static (string? RiskLevel, string? Recommendation) ReadRiskFromReport(string? reportJson)
        {
            if (string.IsNullOrWhiteSpace(reportJson)) return (null, null);
            try
            {
                using var doc = JsonDocument.Parse(reportJson);
                var root = doc.RootElement;
                string? Read(params string[] names)
                {
                    foreach (var name in names)
                    {
                        if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString();
                    }
                    return null;
                }
                return (Read("risk_level", "riskLevel"), Read("recommendation"));
            }
            catch (JsonException)
            {
                return (null, null);
            }
        }
    }
}
