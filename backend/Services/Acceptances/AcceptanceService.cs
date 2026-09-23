using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.Entities;
using LifeLink.DTOs.Planning;
using LifeLink.Services.Planning;
using LifeLink.Services.BloodCompatibility;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Acceptances
{
    public class AcceptanceService : IAcceptanceService
    {
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

            // 1. Blood Request Exists
            var request = await _context.BloodRequests.FindAsync(dto.BloodRequestId);
            if (request == null)
            {
                throw new InvalidOperationException($"Blood request with ID {dto.BloodRequestId} was not found.");
            }

            // 2. Request Not Cancelled
            if (request.CancelledAt != null || request.Status == BloodRequestStatus.Cancelled)
            {
                throw new InvalidOperationException("Cannot accept a cancelled blood request.");
            }

            // 3. Request Not Completed
            if (request.Status == BloodRequestStatus.Completed)
            {
                throw new InvalidOperationException("Cannot accept a completed blood request.");
            }

            // 4. Request Status == Approved
            if (request.Status != BloodRequestStatus.Approved)
            {
                throw new InvalidOperationException("Only approved blood requests can be accepted.");
            }

            // 5. Request Not Expired
            if (request.ExpiryDate <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("Cannot accept an expired blood request.");
            }

            // 6. Request Not Already Fulfilled
            if (request.FulfilledUnits >= request.UnitsRequired)
            {
                throw new InvalidOperationException("This blood request has already been fulfilled.");
            }

            // 7. Donor Is Not Request Owner
            if (request.PatientUserId == donorUserId)
            {
                throw new InvalidOperationException("Donors cannot accept their own blood requests.");
            }

            // 8. Donor Has Not Accepted Before
            var hasAccepted = await _context.Acceptances.AnyAsync(a =>
                a.BloodRequestId == dto.BloodRequestId &&
                a.DonorUserId == donorUserId &&
                a.Status != AcceptanceStatus.Cancelled);

            if (hasAccepted)
            {
                throw new InvalidOperationException("Donor has already accepted this blood request.");
            }

            // 9. Blood Groups Compatible
            if (!_bloodCompatibilityService.IsCompatible(dto.DonorBloodGroup, request.BloodGroup))
            {
                throw new InvalidOperationException(
                    $"Donor blood group '{dto.DonorBloodGroup}' is not compatible with recipient blood group '{request.BloodGroup}'.");
            }

            // 10. Verified Donor Check
            var isVerifiedDonor = await _context.DonorVerifications.AnyAsync(v =>
                (v.AcceptanceId == donorUserId || _context.Acceptances.Any(a => a.AcceptanceId == v.AcceptanceId && a.DonorUserId == donorUserId)) &&
                v.Status == VerificationStatus.Approved);

            if (!isVerifiedDonor)
            {
                throw new InvalidOperationException("Donor is not verified in the verification system.");
            }

            // 11. Request Has Not Already Been Matched Through Student 2
            var alreadyMatched = await _context.DonorPatientMatches.AnyAsync(m =>
                m.BloodRequestId == dto.BloodRequestId &&
                m.Status != MatchStatus.Cancelled);

            if (alreadyMatched)
            {
                throw new InvalidOperationException("Request already fulfilled. Cannot accept additional donors.");
            }

            // 12. Donor Over-Commitment Prevention: Cannot accept if already in an active donation process
            var hasActiveProcess = await _context.Acceptances.AnyAsync(a =>
                a.DonorUserId == donorUserId &&
                (a.Status == AcceptanceStatus.Matched ||
                 a.Status == AcceptanceStatus.ScreeningPending ||
                 a.Status == AcceptanceStatus.Verified ||
                 a.Status == AcceptanceStatus.ScreeningCompleted));

            if (hasActiveProcess)
            {
                throw new InvalidOperationException("You already have an active donation process.");
            }

            // Allow oversubscription beyond UnitsRequired
            var acceptance = new Acceptance
            {
                AcceptanceId = Guid.NewGuid(),
                BloodRequestId = dto.BloodRequestId,
                DonorUserId = donorUserId,
                Status = AcceptanceStatus.Accepted,
                AcceptedAt = DateTime.UtcNow,
                CancelledAt = null,
                RejectionReason = null
            };

            await _context.Acceptances.AddAsync(acceptance);
            await _context.SaveChangesAsync();

            // Trigger Planning Agent for Workflow B: DonorAccepted (orchestrating Agent 1 screening)
            if (_planningAgent != null)
            {
                var planRequest = new PlanRequestDto
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
                };

                await _planningAgent.DispatchPlanAsync(planRequest);
            }

            return MapToResponseDto(acceptance);
        }

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

            acceptance.Status = AcceptanceStatus.Cancelled;
            acceptance.CancelledAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return MapToResponseDto(acceptance);
        }

        public async Task<IEnumerable<AcceptanceResponseDto>> GetMyAcceptancesAsync(Guid donorUserId)
        {
            var acceptances = await _context.Acceptances
                .Where(a => a.DonorUserId == donorUserId)
                .OrderByDescending(a => a.AcceptedAt)
                .ToListAsync();

            return acceptances.Select(MapToResponseDto);
        }

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

        public async Task<FinalizeDonorSelectionResponseDto> FinalizeDonorSelectionAsync(
            Guid bloodRequestId,
            List<Guid> selectedAcceptanceIds,
            Guid doctorUserId)
        {
            if (selectedAcceptanceIds == null || selectedAcceptanceIds.Count == 0)
            {
                throw new ArgumentException("At least one donor must be selected.");
            }

            // 1. BloodRequest must exist
            var request = await _context.BloodRequests.FindAsync(bloodRequestId);
            if (request == null)
            {
                throw new InvalidOperationException($"Blood request with ID {bloodRequestId} was not found.");
            }

            // 2. BloodRequest must not already be Completed
            if (request.Status == BloodRequestStatus.Completed)
            {
                throw new InvalidOperationException("Cannot finalize donor selection on a completed blood request.");
            }

            // 3. BloodRequest Status must be Approved
            if (request.Status != BloodRequestStatus.Approved)
            {
                throw new InvalidOperationException("Only approved blood requests can have donor selections finalized.");
            }

            // Doctor Authorization Validation
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == doctorUserId || d.DoctorId == doctorUserId);
            if (doctor == null || !doctor.IsActive)
            {
                throw new UnauthorizedAccessException("Only verified doctors assigned to this hospital can finalize donor selection.");
            }

            var doctorHospital = await _context.Hospitals.FindAsync(doctor.HospitalId);
            if (doctorHospital == null || !doctorHospital.IsVerified || doctor.HospitalId != request.HospitalId)
            {
                throw new UnauthorizedAccessException("Only verified doctors assigned to this hospital can finalize donor selection.");
            }

            // 4. Number of selected donors cannot exceed remaining units
            var remainingUnits = request.UnitsRequired - request.FulfilledUnits;
            if (selectedAcceptanceIds.Count > remainingUnits)
            {
                throw new InvalidOperationException(
                    $"Selected donors count ({selectedAcceptanceIds.Count}) cannot exceed remaining required units ({remainingUnits}).");
            }

            // Get all acceptances for this request
            var allRequestAcceptances = await _context.Acceptances
                .Where(a => a.BloodRequestId == bloodRequestId)
                .ToListAsync();

            var selectedSet = selectedAcceptanceIds.ToHashSet();
            var selectedAcceptances = allRequestAcceptances.Where(a => selectedSet.Contains(a.AcceptanceId)).ToList();

            // 5. Selected acceptance records must belong to the request
            if (selectedAcceptances.Count != selectedAcceptanceIds.Count)
            {
                throw new InvalidOperationException("One or more selected acceptance IDs do not belong to this blood request.");
            }

            // 6. Selected donors must currently have an eligible status (Accepted, Verified, or ScreeningCompleted)
            if (selectedAcceptances.Any(a => a.Status != AcceptanceStatus.Accepted && a.Status != AcceptanceStatus.Verified && a.Status != AcceptanceStatus.ScreeningCompleted))
            {
                throw new InvalidOperationException("All selected donors must currently have 'Accepted' or 'Verified' status.");
            }

            // 7. Donor already matched through Student 2 DonorPatientMatch cannot be selected again for the same request
            var matchedDonorIds = await _context.DonorPatientMatches
                .Where(m => m.BloodRequestId == bloodRequestId && m.Status != MatchStatus.Cancelled)
                .Select(m => m.DonorUserId)
                .ToListAsync();

            if (selectedAcceptances.Any(a => matchedDonorIds.Contains(a.DonorUserId)))
            {
                throw new InvalidOperationException("One or more selected donors have already been matched for this blood request.");
            }

            // Update selected acceptances to Matched & record fulfillment history audit trail
            foreach (var a in selectedAcceptances)
            {
                a.Status = AcceptanceStatus.Matched;

                var history = new RequestFulfillmentHistory
                {
                    Id = Guid.NewGuid(),
                    BloodRequestId = bloodRequestId,
                    AcceptanceId = a.AcceptanceId,
                    DonorUserId = a.DonorUserId,
                    FulfilledAt = DateTime.UtcNow
                };
                await _context.RequestFulfillmentHistories.AddAsync(history);
            }

            // Concurrency Protection & increment FulfilledUnits
            request.ConcurrencyToken++;
            request.FulfilledUnits += selectedAcceptanceIds.Count;

            int rejectedCount = 0;
            // If request is now completed, reject remaining unselected acceptances
            if (request.FulfilledUnits >= request.UnitsRequired)
            {
                request.Status = BloodRequestStatus.Completed;

                var unselected = allRequestAcceptances
                    .Where(a => !selectedSet.Contains(a.AcceptanceId) && a.Status == AcceptanceStatus.Accepted)
                    .ToList();

                foreach (var a in unselected)
                {
                    a.Status = AcceptanceStatus.Rejected;
                    a.RejectionReason = "Required donor count has been fulfilled by other selected donors.";
                    rejectedCount++;
                }
            }
            else
            {
                request.Status = BloodRequestStatus.Approved;
            }

            request.UpdatedAt = DateTime.UtcNow;

            // Hospital-created requests restock that hospital: collected units go into its inventory.
            // Staged on the same context so the inventory update commits atomically with the selection.
            await AddCollectedUnitsToHospitalInventoryAsync(request, selectedAcceptances.Count);

            await _context.SaveChangesAsync();

            return new FinalizeDonorSelectionResponseDto
            {
                MatchedDonors = selectedAcceptances.Count,
                RejectedDonors = rejectedCount,
                TotalFulfilledUnits = request.FulfilledUnits,
                RemainingUnits = Math.Max(0, request.UnitsRequired - request.FulfilledUnits),
                Message = "Donor selection completed successfully."
            };
        }

        private async Task AddCollectedUnitsToHospitalInventoryAsync(BloodRequest request, int collectedUnits)
        {
            if (collectedUnits <= 0) return;

            var createdByHospital = await _context.UserRoles
                .AnyAsync(ur => ur.UserId == request.PatientUserId && ur.Role.Name == "HospitalStaff");
            if (!createdByHospital) return;

            var now = DateTime.UtcNow;
            var inventory = await _context.BloodInventories
                .FirstOrDefaultAsync(i => i.HospitalId == request.HospitalId && i.BloodGroup == request.BloodGroup);

            if (inventory == null)
            {
                // First stock for this blood group; the hospital can adjust threshold/capacity later
                inventory = new BloodInventory
                {
                    InventoryId = Guid.NewGuid(),
                    HospitalId = request.HospitalId,
                    BloodGroup = request.BloodGroup,
                    UnitsAvailable = 0,
                    MinimumThreshold = 0,
                    MaximumCapacity = 100,
                    CreatedAt = now
                };
                await _context.BloodInventories.AddAsync(inventory);
            }

            // Collected blood is never capped at capacity; discarding donated units would lose real stock
            inventory.UnitsAvailable += collectedUnits;
            inventory.LastUpdated = now;
            inventory.UpdatedAt = now;

            await _context.InventoryTransactions.AddAsync(new InventoryTransaction
            {
                TransactionId = Guid.NewGuid(),
                InventoryId = inventory.InventoryId,
                TransactionType = TransactionType.StockAddition,
                Units = collectedUnits,
                Notes = $"Collected from donors for blood request {request.BloodRequestId}",
                CreatedAt = now
            });
        }

        public async Task<AcceptanceResponseDto> UpdateScreeningStatusAsync(Guid acceptanceId, AcceptanceStatus newStatus)
        {
            var acceptance = await _context.Acceptances.FindAsync(acceptanceId);
            if (acceptance == null)
            {
                throw new KeyNotFoundException($"Acceptance with ID {acceptanceId} was not found.");
            }

            var current = acceptance.Status;
            bool isValidTransition = (current == AcceptanceStatus.Accepted && newStatus == AcceptanceStatus.ScreeningPending) ||
                                     (current == AcceptanceStatus.ScreeningPending && newStatus == AcceptanceStatus.ScreeningCompleted) ||
                                     (current == AcceptanceStatus.ScreeningCompleted && (newStatus == AcceptanceStatus.Verified || newStatus == AcceptanceStatus.Rejected)) ||
                                     (current == AcceptanceStatus.Verified && newStatus == AcceptanceStatus.Matched);

            if (!isValidTransition)
            {
                throw new InvalidOperationException($"Invalid status transition from {current} to {newStatus}.");
            }

            acceptance.Status = newStatus;
            await _context.SaveChangesAsync();

            return MapToResponseDto(acceptance);
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
