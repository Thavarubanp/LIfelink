using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.BloodRequests;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.BloodRequests
{
    public class BloodRequestService : IBloodRequestService
    {
        public const string ExpiryRejectionReason = "Request expired before it was fulfilled.";

        private readonly AppDbContext _context;

        public BloodRequestService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<BloodRequestResponseDto> CreateRequestAsync(Guid patientUserId, CreateBloodRequestDto dto)
        {
            if (patientUserId == Guid.Empty)
            {
                throw new ArgumentException("Invalid patient user ID.");
            }

            if (dto.HospitalId == Guid.Empty)
            {
                throw new ArgumentException("HospitalId is required.");
            }

            if (!BloodValidationHelper.IsValidBloodGroup(dto.BloodGroup))
            {
                throw new ArgumentException($"Invalid blood group: '{dto.BloodGroup}'. Allowed values are O-, O+, A-, A+, B-, B+, AB-, AB+.");
            }

            if (!BloodValidationHelper.IsValidPriority(dto.Priority))
            {
                throw new ArgumentException($"Invalid priority: '{dto.Priority}'. Allowed values are Normal, High, Critical.");
            }

            if (!BloodValidationHelper.IsValidUnits(dto.UnitsRequired))
            {
                throw new ArgumentException("UnitsRequired must be between 1 and 10.");
            }

            if (string.IsNullOrWhiteSpace(dto.Reason))
            {
                throw new ArgumentException("Reason is required.");
            }

            // Verify Hospital exists and is verified
            var hospital = await _context.Hospitals.FindAsync(dto.HospitalId);
            if (hospital == null)
            {
                throw new InvalidOperationException($"Hospital with ID {dto.HospitalId} was not found.");
            }

            if (!hospital.IsVerified)
            {
                throw new InvalidOperationException("Hospital is not verified.");
            }

            var normalizedBloodGroup = BloodValidationHelper.NormalizeBloodGroup(dto.BloodGroup);
            var normalizedPriority = BloodValidationHelper.NormalizePriority(dto.Priority);

            // Duplicate active request check
            var hasActiveDuplicate = await _context.BloodRequests.AnyAsync(r =>
                r.PatientUserId == patientUserId &&
                r.HospitalId == dto.HospitalId &&
                r.BloodGroup == normalizedBloodGroup &&
                (r.Status == BloodRequestStatus.Pending || r.Status == BloodRequestStatus.Verified || r.Status == BloodRequestStatus.Approved));

            if (hasActiveDuplicate)
            {
                throw new InvalidOperationException("An active request already exists for this hospital and blood group.");
            }

            var now = DateTime.UtcNow;
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patientUserId,
                HospitalId = dto.HospitalId,
                BloodGroup = normalizedBloodGroup,
                UnitsRequired = dto.UnitsRequired,
                FulfilledUnits = 0,
                Reason = dto.Reason.Trim(),
                Priority = normalizedPriority,
                Status = BloodRequestStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                ExpiryDate = now.AddDays(7),
                CancelledAt = null
            };

            await _context.BloodRequests.AddAsync(request);
            await _context.SaveChangesAsync();

            return (await MapManyAsync(new[] { request })).First();
        }

        public async Task<IEnumerable<BloodRequestResponseDto>> GetMyRequestsAsync(Guid patientUserId)
        {
            var requests = await _context.BloodRequests
                .Where(r => r.PatientUserId == patientUserId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return await MapManyAsync(requests);
        }

        public async Task<IEnumerable<BloodRequestResponseDto>> GetHospitalRequestsAsync(Guid hospitalId)
        {
            // All statuses, including rejected, so the hospital keeps a full record of requests sent to it
            var requests = await _context.BloodRequests
                .Where(r => r.HospitalId == hospitalId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return await MapManyAsync(requests);
        }

        public async Task<IEnumerable<BloodRequestResponseDto>> GetAssignedRequestsAsync(Guid doctorUserId)
        {
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == doctorUserId);
            if (doctor == null)
            {
                throw new UnauthorizedAccessException("Only doctor accounts can view assigned blood requests.");
            }

            var assignedRequestIds = _context.BloodRequestVerifications
                .Where(v => v.DoctorId == doctor.DoctorId)
                .Select(v => v.BloodRequestId);

            var requests = await _context.BloodRequests
                .Where(r => assignedRequestIds.Contains(r.BloodRequestId))
                .OrderByDescending(r => r.Status == BloodRequestStatus.Verified)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            return await MapManyAsync(requests);
        }

        public async Task DeleteRejectedRequestAsync(Guid requestId, Guid creatorUserId)
        {
            var request = await _context.BloodRequests.FindAsync(requestId);
            if (request == null)
            {
                throw new KeyNotFoundException($"Blood request with ID {requestId} was not found.");
            }

            if (request.PatientUserId != creatorUserId)
            {
                throw new UnauthorizedAccessException("Only the request creator can delete this blood request.");
            }

            if (request.Status != BloodRequestStatus.Rejected)
            {
                throw new InvalidOperationException("Only rejected blood requests can be deleted.");
            }

            // Related tables reference BloodRequestId without FK cascades, so remove them explicitly.
            // A single SaveChanges keeps the whole delete atomic.
            var acceptanceIds = await _context.Acceptances
                .Where(a => a.BloodRequestId == requestId)
                .Select(a => a.AcceptanceId)
                .ToListAsync();

            _context.DonorVerifications.RemoveRange(
                _context.DonorVerifications.Where(v => acceptanceIds.Contains(v.AcceptanceId)));
            _context.RequestFulfillmentHistories.RemoveRange(
                _context.RequestFulfillmentHistories.Where(h => h.BloodRequestId == requestId));
            _context.DonorPatientMatches.RemoveRange(
                _context.DonorPatientMatches.Where(m => m.BloodRequestId == requestId));
            _context.Acceptances.RemoveRange(
                _context.Acceptances.Where(a => a.BloodRequestId == requestId));
            _context.BloodRequestVerifications.RemoveRange(
                _context.BloodRequestVerifications.Where(v => v.BloodRequestId == requestId));
            _context.BloodRequests.Remove(request);

            await _context.SaveChangesAsync();
        }

        public async Task<BloodRequestResponseDto?> GetRequestByIdAsync(Guid requestId)
        {
            var now = DateTime.UtcNow;
            var request = await _context.BloodRequests.FindAsync(requestId);
            if (request == null)
            {
                return null;
            }

            // Automatic expiry check: if expired and not completed/cancelled/rejected, mark Rejected
            if (request.ExpiryDate <= now &&
                request.Status != BloodRequestStatus.Completed &&
                request.Status != BloodRequestStatus.Cancelled &&
                request.Status != BloodRequestStatus.Rejected)
            {
                request.Status = BloodRequestStatus.Rejected;
                request.RejectionReason = ExpiryRejectionReason;
                request.UpdatedAt = now;
                await _context.SaveChangesAsync();
            }

            return (await MapManyAsync(new[] { request })).First();
        }

        public async Task<IEnumerable<BloodRequestResponseDto>> GetPublicRequestsAsync(string? bloodGroup = null, int? expiringWithinHours = null)
        {
            var now = DateTime.UtcNow;
            var query = _context.BloodRequests
                .Where(r => r.Status == BloodRequestStatus.Approved
                            && r.CancelledAt == null
                            && r.ExpiryDate > now
                            && r.FulfilledUnits < r.UnitsRequired);

            if (!string.IsNullOrWhiteSpace(bloodGroup))
            {
                if (BloodValidationHelper.IsValidBloodGroup(bloodGroup))
                {
                    var normalized = BloodValidationHelper.NormalizeBloodGroup(bloodGroup);
                    query = query.Where(r => r.BloodGroup == normalized);
                }
            }

            if (expiringWithinHours.HasValue && expiringWithinHours.Value > 0)
            {
                var threshold = now.AddHours(expiringWithinHours.Value);
                query = query.Where(r => r.ExpiryDate <= threshold);
            }

            var list = await query
                .OrderByDescending(r => r.Priority == "Critical")
                .ThenByDescending(r => r.Priority == "High")
                .ThenBy(r => r.ExpiryDate)
                .ToListAsync();

            return await MapManyAsync(list);
        }

        public async Task<IEnumerable<BloodRequestResponseDto>> GetPendingRequestsAsync(Guid? hospitalId = null)
        {
            var query = _context.BloodRequests
                .Where(r => r.Status == BloodRequestStatus.Pending);

            if (hospitalId.HasValue && hospitalId.Value != Guid.Empty)
            {
                query = query.Where(r => r.HospitalId == hospitalId.Value);
            }

            var list = await query
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return await MapManyAsync(list);
        }

        public async Task<BloodRequestResponseDto> CancelRequestAsync(Guid requestId, Guid patientUserId)
        {
            var request = await _context.BloodRequests.FindAsync(requestId);
            if (request == null)
            {
                throw new KeyNotFoundException($"Blood request with ID {requestId} was not found.");
            }

            if (request.PatientUserId != patientUserId)
            {
                throw new InvalidOperationException("Only the request creator can cancel this blood request.");
            }

            if (request.Status == BloodRequestStatus.Completed)
            {
                throw new InvalidOperationException("Cannot cancel a completed blood request.");
            }

            if (request.Status == BloodRequestStatus.Cancelled)
            {
                throw new InvalidOperationException("Blood request is already cancelled.");
            }

            request.Status = BloodRequestStatus.Cancelled;
            request.CancelledAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return (await MapManyAsync(new[] { request })).First();
        }

        public async Task<BloodRequestAnalyticsDto> GetRequestAnalyticsAsync(Guid requestId)
        {
            var request = await _context.BloodRequests.FindAsync(requestId);
            if (request == null)
            {
                throw new KeyNotFoundException($"Blood request with ID {requestId} was not found.");
            }

            var acceptances = await _context.Acceptances
                .Where(a => a.BloodRequestId == requestId)
                .ToListAsync();

            var acceptanceCount = acceptances.Count;
            var matchedCount = acceptances.Count(a => a.Status == AcceptanceStatus.Matched);
            var rejectedCount = acceptances.Count(a => a.Status == AcceptanceStatus.Rejected);
            var remainingUnits = Math.Max(0, request.UnitsRequired - request.FulfilledUnits);
            var completionPercentage = request.UnitsRequired > 0
                ? (int)Math.Round((double)request.FulfilledUnits / request.UnitsRequired * 100)
                : 0;

            return new BloodRequestAnalyticsDto
            {
                UnitsRequired = request.UnitsRequired,
                FulfilledUnits = request.FulfilledUnits,
                RemainingUnits = remainingUnits,
                AcceptanceCount = acceptanceCount,
                MatchedCount = matchedCount,
                RejectedCount = rejectedCount,
                CompletionPercentage = completionPercentage
            };
        }

        public async Task<IEnumerable<RequestFulfillmentHistoryResponseDto>> GetRequestFulfillmentHistoryAsync(Guid requestId)
        {
            var history = await _context.RequestFulfillmentHistories
                .Where(h => h.BloodRequestId == requestId)
                .OrderByDescending(h => h.FulfilledAt)
                .ToListAsync();

            return history.Select(h => new RequestFulfillmentHistoryResponseDto
            {
                Id = h.Id,
                BloodRequestId = h.BloodRequestId,
                AcceptanceId = h.AcceptanceId,
                DonorUserId = h.DonorUserId,
                FulfilledAt = h.FulfilledAt
            });
        }

        /// <summary>
        /// Maps requests and resolves hospital name, creator name and assigned doctor with one query per table.
        /// </summary>
        private async Task<List<BloodRequestResponseDto>> MapManyAsync(IEnumerable<BloodRequest> requests)
        {
            var list = requests.ToList();
            if (list.Count == 0) return new List<BloodRequestResponseDto>();

            var requestIds = list.Select(r => r.BloodRequestId).ToList();
            var hospitalIds = list.Select(r => r.HospitalId).Distinct().ToList();
            var creatorIds = list.Select(r => r.PatientUserId).Distinct().ToList();

            var hospitalNames = await _context.Hospitals
                .Where(h => hospitalIds.Contains(h.HospitalId))
                .ToDictionaryAsync(h => h.HospitalId, h => h.Name);

            var creatorNames = await _context.Users
                .Where(u => creatorIds.Contains(u.UserId))
                .ToDictionaryAsync(u => u.UserId, u => $"{u.FirstName} {u.LastName}".Trim());

            // Latest verification per request carries the assigned doctor
            var verifications = await _context.BloodRequestVerifications
                .Include(v => v.Doctor)
                .Where(v => requestIds.Contains(v.BloodRequestId))
                .ToListAsync();
            var assignments = verifications
                .GroupBy(v => v.BloodRequestId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.UpdatedAt).First());

            return list.Select(r =>
            {
                var dto = MapToResponseDto(r);
                dto.HospitalName = hospitalNames.GetValueOrDefault(r.HospitalId);
                dto.CreatedByName = creatorNames.GetValueOrDefault(r.PatientUserId);
                if (assignments.TryGetValue(r.BloodRequestId, out var v))
                {
                    dto.AssignedDoctorId = v.DoctorId;
                    dto.AssignedDoctorName = v.Doctor != null
                        ? $"Dr. {v.Doctor.FirstName} {v.Doctor.LastName}".Trim()
                        : (v.DoctorId == null ? "Removed doctor" : null);
                }
                return dto;
            }).ToList();
        }

        private static BloodRequestResponseDto MapToResponseDto(BloodRequest request)
        {
            return new BloodRequestResponseDto
            {
                BloodRequestId = request.BloodRequestId,
                PatientUserId = request.PatientUserId,
                HospitalId = request.HospitalId,
                BloodGroup = request.BloodGroup,
                UnitsRequired = request.UnitsRequired,
                FulfilledUnits = request.FulfilledUnits,
                Reason = request.Reason,
                Priority = request.Priority,
                Status = request.Status.ToString(),
                CreatedAt = request.CreatedAt,
                UpdatedAt = request.UpdatedAt,
                ExpiryDate = request.ExpiryDate,
                CancelledAt = request.CancelledAt,
                RejectionReason = request.RejectionReason
            };
        }
    }
}
