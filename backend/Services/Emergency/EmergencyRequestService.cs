using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Emergency;
using LifeLink.DTOs.Planning;
using LifeLink.Entities;
using LifeLink.Services.Inventory;
using LifeLink.Services.Notification;
using LifeLink.Services.Planning;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Emergency
{
    /// <summary>
    /// Emergency Hub: hospital-to-hospital inventory support. Other approved hospitals that hold compatible,
    /// unexpired packets are alerted and can respond with a transfer offer. Donor emergencies use the normal
    /// Critical blood request workflow instead.
    /// </summary>
    public class EmergencyRequestService : IEmergencyRequestService
    {
        private readonly AppDbContext _context;
        private readonly IPlanningAgentService? _planningAgent;
        private readonly INotificationAgentService? _notificationAgent;

        public EmergencyRequestService(AppDbContext context, IPlanningAgentService? planningAgent = null, INotificationAgentService? notificationAgent = null)
        {
            _context = context;
            _planningAgent = planningAgent;
            _notificationAgent = notificationAgent;
        }

        public async Task<EmergencyRequestResponseDto> CreateEmergencyRequestAsync(EmergencyRequestCreateDto dto)
        {
            if (!BloodGroup.IsValid(dto.BloodGroup))
            {
                throw new InvalidOperationException($"Invalid blood group: '{dto.BloodGroup}'.");
            }

            if (dto.UnitsRequired <= 0)
            {
                throw new InvalidOperationException("UnitsRequired must be greater than zero.");
            }

            if (!Enum.TryParse<EmergencyPriority>(dto.Priority, true, out var parsedPriority))
            {
                throw new InvalidOperationException($"Invalid priority: '{dto.Priority}'. Allowed values: Low, Medium, High, Critical.");
            }

            await EnsureHospitalExistsAsync(dto.HospitalId);

            var now = DateTime.UtcNow;
            var request = new EmergencyRequest
            {
                EmergencyRequestId = Guid.NewGuid(),
                HospitalId = dto.HospitalId,
                BloodGroup = dto.BloodGroup,
                UnitsRequired = dto.UnitsRequired,
                Priority = parsedPriority.ToString(),
                Status = EmergencyRequestStatus.Pending.ToString(),
                Reason = dto.Reason,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.EmergencyRequests.Add(request);
            await _context.SaveChangesAsync();

            await DispatchStockAlertsAsync(request);

            return await MapToResponseDtoAsync(request.EmergencyRequestId);
        }

        /// <summary>
        /// Workflow C (EmergencyShortage): the Supervisor runs the Inventory and Notification agents over the
        /// hospitals the backend found with compatible stock; alerts are saved only for those hospitals.
        /// Without the Supervisor, the same hospitals get a standard alert.
        /// </summary>
        private async Task DispatchStockAlertsAsync(EmergencyRequest request)
        {
            var stockHolders = await FindHospitalsWithCompatibleStockAsync(request);
            var requester = await _context.Hospitals.Where(h => h.HospitalId == request.HospitalId).Select(h => h.Name).FirstOrDefaultAsync() ?? "A partner hospital";

            if (_planningAgent != null)
            {
                var plan = await _planningAgent.DispatchPlanAsync(new PlanRequestDto
                {
                    EventType = "EmergencyShortage",
                    RequestId = request.EmergencyRequestId.ToString(),
                    BloodGroup = request.BloodGroup,
                    Urgency = request.Priority,
                    UnitsRequired = request.UnitsRequired,
                    HospitalId = request.HospitalId.ToString(),
                    Payload = new Dictionary<string, object>
                    {
                        ["requestId"] = request.EmergencyRequestId.ToString(),
                        ["hospitalId"] = request.HospitalId.ToString(),
                        ["hospitalName"] = requester,
                        ["bloodGroup"] = request.BloodGroup,
                        ["unitsRequired"] = request.UnitsRequired,
                        ["priority"] = request.Priority,
                        ["stockHospitals"] = stockHolders.Select(h => new Dictionary<string, object>
                        {
                            ["hospital_id"] = h.HospitalId.ToString(),
                            ["hospital_name"] = h.Name,
                            ["available_units"] = h.Units
                        }).ToList()
                    }
                });

                if (plan != null && plan.Success && _notificationAgent != null)
                {
                    await _notificationAgent.PersistAgentNotificationsAsync(plan.Notifications, new HashSet<Guid>(),
                        stockHolders.Select(h => h.HospitalId).ToHashSet());
                    return;
                }
            }

            foreach (var holder in stockHolders)
            {
                await _context.Notifications.AddAsync(NotificationFactory.ForHospital(holder.HospitalId, "EmergencyStockAlert",
                    $"[{request.Priority.ToUpper()}] Emergency Blood Support Needed ({request.BloodGroup})",
                    $"{requester} urgently needs {request.UnitsRequired} unit(s) of {request.BloodGroup}. You hold {holder.Units} compatible unit(s); consider sending a transfer offer."));
            }
            if (stockHolders.Count > 0)
            {
                await _context.SaveChangesAsync();
            }
        }

        public record StockHolder(Guid HospitalId, string Name, int Units);

        // Red cell compatible donor groups for each recipient group
        private static readonly Dictionary<string, string[]> CompatibleDonorGroups = new()
        {
            ["O-"] = new[] { "O-" },
            ["O+"] = new[] { "O+", "O-" },
            ["A-"] = new[] { "A-", "O-" },
            ["A+"] = new[] { "A+", "A-", "O+", "O-" },
            ["B-"] = new[] { "B-", "O-" },
            ["B+"] = new[] { "B+", "B-", "O+", "O-" },
            ["AB-"] = new[] { "AB-", "A-", "B-", "O-" },
            ["AB+"] = new[] { "AB+", "AB-", "A+", "A-", "B+", "B-", "O+", "O-" }
        };

        /// <summary>Approved, non-suspended hospitals (not the requester) holding unexpired compatible packets.</summary>
        public async Task<List<StockHolder>> FindHospitalsWithCompatibleStockAsync(EmergencyRequest request)
        {
            var now = DateTime.UtcNow;
            var groups = CompatibleDonorGroups.GetValueOrDefault(request.BloodGroup) ?? new[] { request.BloodGroup };
            var stock = await _context.BloodPackets
                .Where(p => p.Status == BloodPacketStatus.Available && p.ExpiryDate > now &&
                            groups.Contains(p.BloodGroup) && p.HospitalId != request.HospitalId &&
                            p.Hospital.IsVerified && !p.Hospital.IsSuspended)
                .GroupBy(p => new { p.HospitalId, p.Hospital.Name })
                .Select(g => new { g.Key.HospitalId, g.Key.Name, Units = g.Count() })
                .ToListAsync();

            return stock.OrderByDescending(s => s.Units).Select(s => new StockHolder(s.HospitalId, s.Name, s.Units)).ToList();
        }

        public async Task<EmergencyRequestResponseDto?> GetEmergencyRequestAsync(Guid id)
        {
            var request = await _context.EmergencyRequests
                .Include(r => r.Hospital)
                .FirstOrDefaultAsync(r => r.EmergencyRequestId == id);

            if (request == null) return null;
            return MapToResponseDto(request);
        }

        public async Task<IEnumerable<EmergencyRequestResponseDto>> GetAllEmergencyRequestsAsync()
        {
            var list = await _context.EmergencyRequests
                .Include(r => r.Hospital)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return list.Select(MapToResponseDto);
        }

        public async Task<EmergencyRequestResponseDto> ApproveEmergencyRequestAsync(Guid id, Guid? actingHospitalId = null)
        {
            var request = await RequireOwnedAsync(id, actingHospitalId);

            if (request.Status == EmergencyRequestStatus.Completed.ToString() || request.Status == EmergencyRequestStatus.Rejected.ToString())
            {
                throw new InvalidOperationException($"Cannot approve an emergency request with status '{request.Status}'.");
            }

            var now = DateTime.UtcNow;
            request.Status = EmergencyRequestStatus.Approved.ToString();
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();
            return await MapToResponseDtoAsync(request.EmergencyRequestId);
        }

        public async Task<EmergencyRequestResponseDto> RejectEmergencyRequestAsync(Guid id, Guid? actingHospitalId = null)
        {
            var request = await RequireOwnedAsync(id, actingHospitalId);

            if (request.Status == EmergencyRequestStatus.Completed.ToString())
            {
                throw new InvalidOperationException("Cannot reject a completed emergency request.");
            }

            var now = DateTime.UtcNow;
            request.Status = EmergencyRequestStatus.Rejected.ToString();
            request.UpdatedAt = now;

            await _context.SaveChangesAsync();
            return await MapToResponseDtoAsync(request.EmergencyRequestId);
        }

        public async Task<EmergencyRequestResponseDto> CompleteEmergencyRequestAsync(Guid id, Guid? actingHospitalId = null, Guid? performedByUserId = null)
        {
            var request = await RequireOwnedAsync(id, actingHospitalId);

            if (request.Status == EmergencyRequestStatus.Rejected.ToString())
            {
                throw new InvalidOperationException("Emergency requests cannot be completed if status is rejected.");
            }

            if (request.Status == EmergencyRequestStatus.Completed.ToString())
            {
                throw new InvalidOperationException("Emergency request is already completed.");
            }

            var now = DateTime.UtcNow;
            request.Status = EmergencyRequestStatus.Completed.ToString();
            request.UpdatedAt = now;

            // Dispatch from own stock when it covers the need (earliest-expiring packets first, each one audited)
            var available = await InventoryLedger.CountAvailableAsync(_context, request.HospitalId, request.BloodGroup);
            if (available >= request.UnitsRequired)
            {
                await InventoryLedger.IssuePacketsAsync(_context, request.HospitalId, request.BloodGroup, request.UnitsRequired,
                    request.EmergencyRequestId, $"Emergency request '{id}' completed and dispatched.", performedByUserId);
            }

            await _context.SaveChangesAsync();
            return await MapToResponseDtoAsync(request.EmergencyRequestId);
        }

        public async Task<IEnumerable<EmergencyRequestResponseDto>> GetCriticalEmergencyRequestsAsync()
        {
            var list = await _context.EmergencyRequests
                .Include(r => r.Hospital)
                .Where(r => r.Priority == EmergencyPriority.Critical.ToString())
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return list.Select(MapToResponseDto);
        }

        private async Task<EmergencyRequest> RequireOwnedAsync(Guid id, Guid? actingHospitalId)
        {
            var request = await _context.EmergencyRequests.FindAsync(id);
            if (request == null)
            {
                throw new KeyNotFoundException($"Emergency request with ID '{id}' was not found.");
            }

            if (actingHospitalId.HasValue && request.HospitalId != actingHospitalId.Value)
            {
                throw new UnauthorizedAccessException("Only the hospital that raised this emergency can update it.");
            }

            return request;
        }

        private async Task EnsureHospitalExistsAsync(Guid hospitalId)
        {
            // An empty ID would make EF generate a fresh key, adding a new "Hospital 00000000" placeholder on every call
            if (hospitalId == Guid.Empty)
                throw new InvalidOperationException("A valid hospital ID is required.");

            var hospital = await _context.Hospitals.FindAsync(hospitalId);
            if (hospital == null)
            {
                var now = DateTime.UtcNow;
                hospital = new Hospital
                {
                    HospitalId = hospitalId,
                    Name = $"Hospital {hospitalId.ToString()[..8]}",
                    LicenseNumber = $"LIC-{hospitalId.ToString()[..6].ToUpper()}",
                    Address = "Default Address",
                    ContactNumber = "+1000000000",
                    Email = $"hospital_{hospitalId.ToString()[..8]}@lifelink.org",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.Hospitals.Add(hospital);
                await _context.SaveChangesAsync();
            }
        }

        private async Task<EmergencyRequestResponseDto> MapToResponseDtoAsync(Guid id)
        {
            var request = await _context.EmergencyRequests
                .Include(r => r.Hospital)
                .FirstAsync(r => r.EmergencyRequestId == id);

            return MapToResponseDto(request);
        }

        private static EmergencyRequestResponseDto MapToResponseDto(EmergencyRequest r)
        {
            return new EmergencyRequestResponseDto
            {
                EmergencyRequestId = r.EmergencyRequestId,
                HospitalId = r.HospitalId,
                HospitalName = r.Hospital != null ? r.Hospital.Name : string.Empty,
                BloodGroup = r.BloodGroup,
                UnitsRequired = r.UnitsRequired,
                Priority = r.Priority,
                Status = r.Status,
                Reason = r.Reason,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            };
        }
    }
}
