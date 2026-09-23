using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

            // Trigger Planning Agent for Workflow A: BloodRequestApproved (with resilient fallback to Agent 2)
            var bloodGroup = request.BloodGroup;
            var hospitalId = request.HospitalId;
            var priority = !string.IsNullOrWhiteSpace(request.Priority) ? request.Priority : "Normal";

            bool planDispatched = false;
            if (_planningAgent != null)
            {
                var planRequest = new PlanRequestDto
                {
                    EventType = "BloodRequestApproved",
                    RequestId = verification.BloodRequestId.ToString(),
                    BloodGroup = bloodGroup,
                    Urgency = priority,
                    HospitalId = hospitalId.ToString(),
                    UnitsRequired = request.UnitsRequired,
                    Payload = new Dictionary<string, object>
                    {
                        ["requestId"] = verification.BloodRequestId.ToString(),
                        ["bloodGroup"] = bloodGroup,
                        ["hospitalId"] = hospitalId.ToString(),
                        ["priority"] = priority
                    }
                };

                var planResult = await _planningAgent.DispatchPlanAsync(planRequest);
                if (planResult != null && planResult.Success)
                {
                    planDispatched = true;
                }
            }

            // Fallback directly to Agent 2 notification processing per resilience pattern if Planning Agent offline/unsuccessful
            if (!planDispatched)
            {
                await _notificationAgent.ProcessRequestApprovalNotificationAsync(verification.BloodRequestId, bloodGroup, hospitalId, priority);
            }

            return MapVerification(verification, doctor);
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

        public async Task<DonorVerificationResponseDto> ApproveDonorVerificationAsync(Guid id, ApproveRejectRequestDto dto)
        {
            var doctor = await _context.Doctors.FindAsync(dto.DoctorId);
            if (doctor == null)
            {
                throw new InvalidOperationException($"Doctor with ID {dto.DoctorId} was not found.");
            }

            var verification = await _context.DonorVerifications
                .FirstOrDefaultAsync(v => v.AcceptanceId == id || v.DonorVerificationId == id);

            if (verification == null)
            {
                verification = new DonorVerification
                {
                    DonorVerificationId = Guid.NewGuid(),
                    AcceptanceId = id,
                    DoctorId = dto.DoctorId,
                    Status = VerificationStatus.Approved,
                    MedicalReportSummary = dto.MedicalReportSummary,
                    Notes = dto.Notes,
                    VerifiedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _context.DonorVerifications.AddAsync(verification);
            }
            else
            {
                verification.DoctorId = dto.DoctorId;
                verification.Status = VerificationStatus.Approved;
                verification.MedicalReportSummary = dto.MedicalReportSummary;
                verification.Notes = dto.Notes;
                verification.VerifiedAt = DateTime.UtcNow;
                verification.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return new DonorVerificationResponseDto
            {
                DonorVerificationId = verification.DonorVerificationId,
                AcceptanceId = verification.AcceptanceId,
                DoctorId = verification.DoctorId,
                DoctorName = $"{doctor.FirstName} {doctor.LastName}",
                Status = verification.Status.ToString(),
                MedicalReportSummary = verification.MedicalReportSummary,
                Notes = verification.Notes,
                VerifiedAt = verification.VerifiedAt,
                CreatedAt = verification.CreatedAt
            };
        }

        public async Task<DonorVerificationResponseDto> RejectDonorVerificationAsync(Guid id, ApproveRejectRequestDto dto)
        {
            var doctor = await _context.Doctors.FindAsync(dto.DoctorId);
            if (doctor == null)
            {
                throw new InvalidOperationException($"Doctor with ID {dto.DoctorId} was not found.");
            }

            var verification = await _context.DonorVerifications
                .FirstOrDefaultAsync(v => v.AcceptanceId == id || v.DonorVerificationId == id);

            if (verification == null)
            {
                verification = new DonorVerification
                {
                    DonorVerificationId = Guid.NewGuid(),
                    AcceptanceId = id,
                    DoctorId = dto.DoctorId,
                    Status = VerificationStatus.Rejected,
                    MedicalReportSummary = dto.MedicalReportSummary,
                    Notes = dto.Notes,
                    VerifiedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _context.DonorVerifications.AddAsync(verification);
            }
            else
            {
                verification.DoctorId = dto.DoctorId;
                verification.Status = VerificationStatus.Rejected;
                verification.MedicalReportSummary = dto.MedicalReportSummary;
                verification.Notes = dto.Notes;
                verification.VerifiedAt = DateTime.UtcNow;
                verification.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return new DonorVerificationResponseDto
            {
                DonorVerificationId = verification.DonorVerificationId,
                AcceptanceId = verification.AcceptanceId,
                DoctorId = verification.DoctorId,
                DoctorName = $"{doctor.FirstName} {doctor.LastName}",
                Status = verification.Status.ToString(),
                MedicalReportSummary = verification.MedicalReportSummary,
                Notes = verification.Notes,
                VerifiedAt = verification.VerifiedAt,
                CreatedAt = verification.CreatedAt
            };
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

        public async Task<List<DonorVerificationResponseDto>> GetDonorVerificationsAsync()
        {
            var list = await _context.DonorVerifications
                .Include(v => v.Doctor)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();

            return list.Select(v => new DonorVerificationResponseDto
            {
                DonorVerificationId = v.DonorVerificationId,
                AcceptanceId = v.AcceptanceId,
                DoctorId = v.DoctorId,
                DoctorName = v.Doctor != null ? $"{v.Doctor.FirstName} {v.Doctor.LastName}" : string.Empty,
                Status = v.Status.ToString(),
                MedicalReportSummary = v.MedicalReportSummary,
                Notes = v.Notes,
                VerifiedAt = v.VerifiedAt,
                CreatedAt = v.CreatedAt
            }).ToList();
        }
    }
}
