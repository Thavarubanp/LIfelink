using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.BloodRequests;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using LifeLink.Services.Common;
using LifeLink.Services.Inventory;
using LifeLink.Services.Notification;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.BloodRequests
{
    public class BloodRequestService : IBloodRequestService
    {
        // Retained only for backward compatibility with historical rows and activity data. Blood requests no longer
        // expire automatically; the non-null database column remains without requiring a migration.
        public const string ExpiryRejectionReason = "Request expired before it was fulfilled.";
        private static readonly DateTime NoExpirySentinel = new(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc);

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

            if (hospital.IsSuspended)
            {
                throw new InvalidOperationException("This hospital is suspended. Please choose another active hospital.");
            }

            // Permanently blocked / deleted accounts never create requests again
            if (await _context.Users.AnyAsync(u => u.UserId == patientUserId &&
                    (u.AccountStatus == AccountStatus.Blocked || u.AccountStatus == AccountStatus.Deleted)))
            {
                throw new InvalidOperationException("This account can no longer create blood requests.");
            }

            // A hospital's own request names one of its doctors, who approves it (and any hospital donation to it)
            Doctor? assignedDoctor = null;
            if (await _context.UserRoles.AnyAsync(ur => ur.UserId == patientUserId && ur.Role.Name == "HospitalStaff"))
            {
                assignedDoctor = await DoctorAssignmentRules.RequireAssignableDoctorAsync(_context, dto.DoctorId, dto.HospitalId,
                    "Select the doctor who will approve this request.");
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
                // Hospital requests skip self-verification: they go straight to the chosen doctor
                Status = assignedDoctor != null ? BloodRequestStatus.Verified : BloodRequestStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                ExpiryDate = NoExpirySentinel,
                CancelledAt = null
            };

            await _context.BloodRequests.AddAsync(request);
            if (assignedDoctor != null)
            {
                await _context.BloodRequestVerifications.AddAsync(new BloodRequestVerification
                {
                    VerificationId = Guid.NewGuid(),
                    BloodRequestId = request.BloodRequestId,
                    DoctorId = assignedDoctor.DoctorId,
                    Status = VerificationStatus.Pending,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            await ActivityLogger.AddAsync(_context, patientUserId, "BloodRequest.Created", ActivityLogger.Types.BloodRequest, request.BloodRequestId,
                $"Created blood request #{NotificationFactory.ShortId(request.BloodRequestId)} for {request.BloodGroup}, {request.UnitsRequired} unit(s), {request.Priority} priority, at {hospital.Name}" +
                (assignedDoctor != null ? $" (assigned to Dr. {assignedDoctor.FirstName} {assignedDoctor.LastName})." : "."));
            await _context.SaveChangesAsync();

            return (await MapManyAsync(new[] { request })).First();
        }

        /// <summary>
        /// The patient who created a request changes its blood group and units while it is still Pending (before the
        /// hospital verifies or rejects it). Only those two fields change, with the same validation as creation.
        /// </summary>
        public async Task<BloodRequestResponseDto> UpdatePendingRequestAsync(Guid requestId, Guid patientUserId, UpdateBloodRequestDto dto)
        {
            var request = await _context.BloodRequests.FindAsync(requestId)
                          ?? throw new KeyNotFoundException($"Blood request with ID {requestId} was not found.");

            if (request.PatientUserId != patientUserId)
            {
                throw new UnauthorizedAccessException("Only the patient who created this request can edit it.");
            }
            SuspensionGuard.EnsureNotSuspended(request);

            if (request.Status != BloodRequestStatus.Pending)
            {
                throw new InvalidOperationException($"Only pending requests can be edited. This request is {request.Status}.");
            }

            if (!BloodValidationHelper.IsValidBloodGroup(dto.BloodGroup))
            {
                throw new ArgumentException($"Invalid blood group: '{dto.BloodGroup}'. Allowed values are O-, O+, A-, A+, B-, B+, AB-, AB+.");
            }

            if (!BloodValidationHelper.IsValidUnits(dto.UnitsRequired))
            {
                throw new ArgumentException("UnitsRequired must be between 1 and 10.");
            }

            if (await _context.Users.AnyAsync(u => u.UserId == patientUserId &&
                    (u.AccountStatus == AccountStatus.Blocked || u.AccountStatus == AccountStatus.Deleted)))
            {
                throw new InvalidOperationException("This account can no longer change blood requests.");
            }

            var normalizedBloodGroup = BloodValidationHelper.NormalizeBloodGroup(dto.BloodGroup);

            // Same duplicate rule as creation, ignoring the request being edited
            var hasActiveDuplicate = await _context.BloodRequests.AnyAsync(r =>
                r.BloodRequestId != requestId &&
                r.PatientUserId == patientUserId &&
                r.HospitalId == request.HospitalId &&
                r.BloodGroup == normalizedBloodGroup &&
                (r.Status == BloodRequestStatus.Pending || r.Status == BloodRequestStatus.Verified || r.Status == BloodRequestStatus.Approved));
            if (hasActiveDuplicate)
            {
                throw new InvalidOperationException("An active request already exists for this hospital and blood group.");
            }

            await ActivityLogger.AddAsync(_context, patientUserId, "BloodRequest.Edited", ActivityLogger.Types.BloodRequest, request.BloodRequestId,
                $"Edited blood request #{NotificationFactory.ShortId(request.BloodRequestId)}: {request.BloodGroup}, {request.UnitsRequired} unit(s) -> {normalizedBloodGroup}, {dto.UnitsRequired} unit(s).");
            request.BloodGroup = normalizedBloodGroup;
            request.UnitsRequired = dto.UnitsRequired;
            request.UpdatedAt = DateTime.UtcNow;

            // A hospital verifying or rejecting at the same moment changes the concurrency token: one save fails (409)
            await _context.SaveChangesAsync();

            return (await MapManyAsync(new[] { request })).First();
        }

        public async Task<IEnumerable<BloodRequestResponseDto>> GetMyRequestsAsync(Guid patientUserId)
        {
            // Deleted requests are hidden from everyone except the Admin
            var requests = await _context.BloodRequests
                .Where(r => r.PatientUserId == patientUserId && r.Status != BloodRequestStatus.Deleted)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return await MapManyAsync(requests);
        }

        public async Task<IEnumerable<BloodRequestResponseDto>> GetHospitalRequestsAsync(Guid hospitalId)
        {
            // All statuses, including rejected, so the hospital keeps a full record of requests sent to it (deleted
            // requests are hidden from everyone except the Admin)
            var requests = await _context.BloodRequests
                .Where(r => r.HospitalId == hospitalId && r.Status != BloodRequestStatus.Deleted)
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

            // A doctor still pending first login is never a fallback decider (5.1)
            var fallbackRequestIds = doctor.IsActive && DoctorAssignmentRules.HasCompletedFirstLogin(doctor)
                ? await GetFallbackDonationRequestIdsAsync(doctor.HospitalId)
                : new List<Guid>();

            var requests = await _context.BloodRequests
                .Where(r => (assignedRequestIds.Contains(r.BloodRequestId) || fallbackRequestIds.Contains(r.BloodRequestId)) &&
                            r.Status != BloodRequestStatus.Deleted)
                .OrderByDescending(r => r.Status == BloodRequestStatus.Verified)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            return await MapManyAsync(requests);
        }

        /// <summary>
        /// Requests of a hospital with hospital donations waiting for a decision whose assigned doctor was removed (or is
        /// no longer active). Any active doctor of that hospital may decide on them, so they show on those doctors' lists.
        /// </summary>
        private async Task<List<Guid>> GetFallbackDonationRequestIdsAsync(Guid hospitalId)
        {
            var candidateIds = await _context.BloodRequests
                .Where(r => r.HospitalId == hospitalId && _context.Acceptances.Any(a =>
                    a.BloodRequestId == r.BloodRequestId && a.DonorHospitalId != null && a.Status == AcceptanceStatus.Accepted))
                .Select(r => r.BloodRequestId)
                .ToListAsync();
            if (candidateIds.Count == 0) return candidateIds;

            // The assigned doctor is the latest verification's doctor (as for the approval rule)
            var latestDoctorByRequest = (await _context.BloodRequestVerifications
                    .Where(v => candidateIds.Contains(v.BloodRequestId) && v.Status != VerificationStatus.Closed)
                    .ToListAsync())
                .GroupBy(v => v.BloodRequestId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.UpdatedAt).First().DoctorId);
            var activeDoctorIds = await _context.Doctors
                .Where(d => d.HospitalId == hospitalId && d.IsActive)
                .Select(d => d.DoctorId)
                .ToListAsync();

            return candidateIds
                .Where(id => !(latestDoctorByRequest.GetValueOrDefault(id) is Guid assigned && activeDoctorIds.Contains(assigned)))
                .ToList();
        }

        public const string DeletedRequestMessage = "This blood request was deleted by its creator.";
        public const string AcceptanceClosedByDeleteReason = "Closed – the request was deleted.";

        /// <summary>Acceptance statuses of a donor or hospital donation still taking part in a request (or donated).</summary>
        public static readonly AcceptanceStatus[] InProgressAcceptanceStatuses =
        {
            AcceptanceStatus.Accepted,
            AcceptanceStatus.ScreeningPending,
            AcceptanceStatus.ScreeningCompleted,
            AcceptanceStatus.Verified,
            AcceptanceStatus.Matched
        };

        /// <summary>
        /// The creator deletes their request, in any status, even while donors or hospital donations are active. It is a
        /// soft delete: nothing is removed. In one save the request becomes Deleted (hidden from every list except the
        /// Admin's), active acceptances are closed (a reserved slot is freed, a pending screening report version is
        /// closed but kept, and a hospital donation's held packets go back to that hospital's inventory), open matches
        /// are cancelled, and the hospital, the assigned doctor and the donors/hospitals whose acceptances were closed
        /// are notified. Donated (Matched) acceptances, screening reports, fulfilment history and packets are kept.
        /// A donor or hospital accepting at the same moment makes one of the two fail with 409 (both change the
        /// request's concurrency token). No AI agent is involved.
        /// </summary>
        public async Task DeleteRequestAsync(Guid requestId, Guid creatorUserId)
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

            if (request.Status == BloodRequestStatus.Deleted)
            {
                throw new InvalidOperationException("This blood request has already been deleted.");
            }

            var now = DateTime.UtcNow;
            var closed = await AcceptanceClosure.CloseAllAsync(_context, request, AcceptanceClosure.ActiveStatuses,
                AcceptanceStatus.Cancelled, AcceptanceClosedByDeleteReason, "Request deleted by its creator.");

            var openMatches = await _context.DonorPatientMatches
                .Where(m => m.BloodRequestId == requestId && m.Status != MatchStatus.Cancelled && m.Status != MatchStatus.Completed)
                .ToListAsync();
            foreach (var match in openMatches)
            {
                match.Status = MatchStatus.Cancelled;
                match.UpdatedAt = now;
            }

            await ActivityLogger.AddAsync(_context, creatorUserId, "BloodRequest.Deleted", ActivityLogger.Types.BloodRequest, request.BloodRequestId,
                $"Deleted blood request #{NotificationFactory.ShortId(request.BloodRequestId)} ({request.BloodGroup}, {request.UnitsRequired} unit(s), was {request.Status})" +
                (closed.Count > 0 ? $"; {closed.Count} active donor(s)/donation(s) were released." : ".") +
                (SuspensionGuard.IsSuspended(request) ? " It was suspended by the administrator at the time." : string.Empty));
            request.Status = BloodRequestStatus.Deleted;
            request.DeletedAt = now;
            request.UpdatedAt = now;

            await AddDeletionNotificationsAsync(request, closed);

            // Owner's Q7: the creator may delete a request the admin suspended; it stays visible to the admin as Deleted + Suspended
            if (SuspensionGuard.IsSuspended(request))
            {
                await SuspensionGuard.NotifyAdminsAsync(_context, "SuspendedRequestDeleted", "Suspended Blood Request Deleted",
                    $"The creator deleted blood request #{NotificationFactory.ShortId(request.BloodRequestId)} ({request.BloodGroup}, {request.UnitsRequired} unit(s)) while it was suspended.");
            }

            // One SaveChanges = one transaction: the request, the closed acceptances, released packets and the
            // notifications commit together or not at all
            await InventoryLedger.SavePacketChangesAsync(_context,
                "This request changed while it was being deleted (for example, a donor has just accepted it). Refresh and try again.");
        }

        // Staged on the same context as the delete so both commit (or fail) together. The recipients can no longer open
        // the request, so each message carries the blood group, units, hospital and creation date.
        private async Task AddDeletionNotificationsAsync(BloodRequest request, List<Acceptance> closedAcceptances)
        {
            var hospitalName = await _context.Hospitals
                .Where(h => h.HospitalId == request.HospitalId)
                .Select(h => h.Name)
                .FirstOrDefaultAsync() ?? "the hospital";
            // Creation date as the Sri Lanka calendar date (same rule as packet dates), not the UTC date
            var created = PacketDateRules.Today(request.CreatedAt);
            var summary = $"Blood request for {request.BloodGroup}, {request.UnitsRequired} unit(s) at {hospitalName} (created {created:d MMM yyyy}) was deleted by the requester.";
            const string type = "BloodRequestDeleted";
            const string title = "Blood Request Deleted";

            var notifications = new List<LifeLink.Entities.Notification>();

            // The hospital the request belongs to is always operationally affected, including hospital-created requests.
            notifications.Add(NotificationFactory.ForHospital(request.HospitalId, type, title, summary));

            // The assigned doctor (latest assignment that is not closed), if the doctor still has an active login
            var assignedDoctorId = await _context.BloodRequestVerifications
                .Where(v => v.BloodRequestId == request.BloodRequestId && v.Status != VerificationStatus.Closed)
                .OrderByDescending(v => v.UpdatedAt)
                .Select(v => v.DoctorId)
                .FirstOrDefaultAsync();
            if (assignedDoctorId.HasValue)
            {
                var doctorUserId = await _context.Doctors
                    .Where(d => d.DoctorId == assignedDoctorId.Value && d.IsActive && d.DeletedAt == null && d.UserId != null)
                    .Select(d => d.UserId)
                    .FirstOrDefaultAsync();
                if (doctorUserId.HasValue && doctorUserId.Value != request.PatientUserId)
                {
                    notifications.Add(NotificationFactory.ForUser(doctorUserId.Value, "Doctor", type, title, summary));
                }
            }

            // Donors and hospitals whose active acceptances were closed by the delete
            foreach (var donorId in closedAcceptances.Where(a => a.DonorHospitalId == null).Select(a => a.DonorUserId)
                         .Where(id => id != request.PatientUserId).Distinct())
            {
                notifications.Add(NotificationFactory.ForUser(donorId, "Donor", type, title,
                    $"{summary} Your acceptance has been closed and you are free to accept other requests."));
            }
            foreach (var hospitalId in closedAcceptances.Where(a => a.DonorHospitalId != null).Select(a => a.DonorHospitalId!.Value)
                         .Where(id => id != request.HospitalId).Distinct())
            {
                notifications.Add(NotificationFactory.ForHospital(hospitalId, type, title,
                    $"{summary} Your donation offer has been closed and the reserved packets are back in your inventory."));
            }

            await _context.Notifications.AddRangeAsync(notifications);
        }

        public async Task<BloodRequestResponseDto?> GetRequestByIdAsync(Guid requestId)
        {
            var request = await _context.BloodRequests.FindAsync(requestId);
            if (request == null)
            {
                return null;
            }

            return (await MapManyAsync(new[] { request })).First();
        }

        public async Task<IEnumerable<BloodRequestResponseDto>> GetPublicRequestsAsync(string? bloodGroup = null, int? expiringWithinHours = null)
        {
            var query = _context.BloodRequests
                .Where(r => r.Status == BloodRequestStatus.Approved
                            && r.CancelledAt == null
                            && r.FulfilledUnits < r.UnitsRequired
                            && r.AdminSuspendedAt == null // suspended by the admin (Q7): hidden from donors
                            // A suspended hospital's requests cannot proceed, so donors do not see them
                            && !_context.Hospitals.Any(h => h.HospitalId == r.HospitalId && h.IsSuspended));

            if (!string.IsNullOrWhiteSpace(bloodGroup))
            {
                if (BloodValidationHelper.IsValidBloodGroup(bloodGroup))
                {
                    var normalized = BloodValidationHelper.NormalizeBloodGroup(bloodGroup);
                    query = query.Where(r => r.BloodGroup == normalized);
                }
            }

            var list = await query
                .OrderByDescending(r => r.Priority == "Critical")
                .ThenByDescending(r => r.Priority == "High")
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            return await MapManyAsync(list);
        }

        /// <summary>Every blood request (any creator, any status, including deleted ones), newest first: Admin oversight only.</summary>
        public async Task<IEnumerable<BloodRequestResponseDto>> GetAllRequestsForAdminAsync()
        {
            var list = await _context.BloodRequests.OrderByDescending(r => r.CreatedAt).ToListAsync();
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
            SuspensionGuard.EnsureNotSuspended(request);

            if (request.Status == BloodRequestStatus.Completed)
            {
                throw new InvalidOperationException("Cannot cancel a completed blood request.");
            }

            if (request.Status == BloodRequestStatus.Cancelled)
            {
                throw new InvalidOperationException("Blood request is already cancelled.");
            }

            if (request.Status == BloodRequestStatus.Deleted)
            {
                throw new InvalidOperationException("This blood request has been deleted.");
            }

            await AcceptanceClosure.CloseAllAsync(_context, request, AcceptanceClosure.ActiveStatuses,
                AcceptanceStatus.Cancelled, "The request was cancelled by its creator.", "Request cancelled by its creator.");

            request.Status = BloodRequestStatus.Cancelled;
            request.CancelledAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;

            await ActivityLogger.AddAsync(_context, patientUserId, "BloodRequest.Cancelled", ActivityLogger.Types.BloodRequest, request.BloodRequestId,
                $"Cancelled blood request #{NotificationFactory.ShortId(request.BloodRequestId)} ({request.BloodGroup}, {request.UnitsRequired} unit(s)).");
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
                ReservedUnits = request.ReservedUnits,
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

            var hospitals = await _context.Hospitals
                .Where(h => hospitalIds.Contains(h.HospitalId))
                .Select(h => new { h.HospitalId, h.Name, h.IsSuspended })
                .ToDictionaryAsync(h => h.HospitalId);
            var now = DateTime.UtcNow;

            var creatorNames = await _context.Users
                .Where(u => creatorIds.Contains(u.UserId))
                .ToDictionaryAsync(u => u.UserId, u => $"{u.FirstName} {u.LastName}".Trim());

            // Latest verification per request carries the assigned doctor
            var verifications = await _context.BloodRequestVerifications
                .Include(v => v.Doctor)
                .Where(v => requestIds.Contains(v.BloodRequestId) && v.Status != VerificationStatus.Closed)
                .ToListAsync();
            var assignments = verifications
                .GroupBy(v => v.BloodRequestId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.UpdatedAt).First());

            var requestAcceptances = await _context.Acceptances
                .Where(a => requestIds.Contains(a.BloodRequestId))
                .Select(a => new { a.AcceptanceId, a.BloodRequestId, a.Status })
                .ToListAsync();
            var activeRequestIds = requestAcceptances.Where(a => InProgressAcceptanceStatuses.Contains(a.Status)).Select(a => a.BloodRequestId).ToHashSet();
            var allAcceptanceIds = requestAcceptances.Select(a => a.AcceptanceId).ToList();
            var screenedAcceptanceIds = allAcceptanceIds.Count == 0
                ? new HashSet<Guid>()
                : (await _context.DonorVerifications.Where(v => allAcceptanceIds.Contains(v.AcceptanceId)).Select(v => v.AcceptanceId).Distinct().ToListAsync()).ToHashSet();
            var screenedRequestIds = requestAcceptances.Where(a => screenedAcceptanceIds.Contains(a.AcceptanceId)).Select(a => a.BloodRequestId).ToHashSet();

            var pendingHospitalDonations = await _context.Acceptances
                .Where(a => requestIds.Contains(a.BloodRequestId) && a.DonorHospitalId != null && a.Status == AcceptanceStatus.Accepted)
                .GroupBy(a => a.BloodRequestId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            return list.Select(r =>
            {
                var dto = MapToResponseDto(r);
                var hospital = hospitals.GetValueOrDefault(r.HospitalId);
                dto.HospitalName = hospital?.Name;
                dto.IsAcceptingDonors = r.Status == BloodRequestStatus.Approved &&
                                        r.FulfilledUnits + r.ReservedUnits < r.UnitsRequired && hospital?.IsSuspended != true;
                dto.CreatedByName = creatorNames.GetValueOrDefault(r.PatientUserId);
                dto.PendingHospitalDonations = pendingHospitalDonations.GetValueOrDefault(r.BloodRequestId);
                dto.HasActiveAcceptances = activeRequestIds.Contains(r.BloodRequestId);
                dto.HasScreenedDonors = screenedRequestIds.Contains(r.BloodRequestId);
                if (assignments.TryGetValue(r.BloodRequestId, out var v))
                {
                    dto.AssignedDoctorId = v.DoctorId;
                    dto.AssignedDoctorName = v.Doctor != null && v.Doctor.DeletedAt == null
                        ? $"Dr. {v.Doctor.FirstName} {v.Doctor.LastName}".Trim()
                        : (v.DoctorId == null || v.Doctor?.DeletedAt != null ? "Removed doctor" : null);
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
                ReservedUnits = request.ReservedUnits,
                Reason = request.Reason,
                Priority = request.Priority,
                Status = request.Status.ToString(),
                CreatedAt = request.CreatedAt,
                UpdatedAt = request.UpdatedAt,
                ExpiryDate = request.ExpiryDate,
                CancelledAt = request.CancelledAt,
                DeletedAt = request.DeletedAt,
                IsSuspended = request.AdminSuspendedAt != null,
                SuspendedAt = request.AdminSuspendedAt,
                SuspensionReason = request.AdminSuspensionReason,
                RejectionReason = request.RejectionReason
            };
        }
    }
}
