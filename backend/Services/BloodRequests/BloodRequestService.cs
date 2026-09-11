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
                (r.Status == BloodRequestStatus.Pending || r.Status == BloodRequestStatus.Approved));

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

            return MapToResponseDto(request);
        }

        public async Task<IEnumerable<BloodRequestResponseDto>> GetMyRequestsAsync(Guid patientUserId)
        {
            var requests = await _context.BloodRequests
                .Where(r => r.PatientUserId == patientUserId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return requests.Select(MapToResponseDto);
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
                request.UpdatedAt = now;
                await _context.SaveChangesAsync();
            }

            return MapToResponseDto(request);
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

            return list.Select(MapToResponseDto);
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

            return list.Select(MapToResponseDto);
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

            return MapToResponseDto(request);
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
                CancelledAt = request.CancelledAt
            };
        }
    }
}
