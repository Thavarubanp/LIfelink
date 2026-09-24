using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Planning;
using LifeLink.Entities;
using LifeLink.Services.Notification;
using LifeLink.Services.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.Inventory
{
    /// <summary>
    /// Scheduled threshold and expiry monitoring. The backend gathers each approved hospital's stock, expiring
    /// packets and open public requests, and the Supervisor runs the Inventory agent (shortages, surpluses,
    /// expiring stock that other hospitals or requests could use) and the Notification agent (hospital alerts).
    /// Alerts are saved only for approved, non-suspended hospitals. Without the Supervisor, simple rule-based
    /// alerts are sent instead. Repeated unread alerts are suppressed for 12 hours.
    /// </summary>
    public class InventoryMonitor
    {
        private readonly AppDbContext _context;
        private readonly IPlanningAgentService _planningAgent;
        private readonly INotificationAgentService _notificationAgent;
        private readonly ILogger<InventoryMonitor> _logger;

        public InventoryMonitor(AppDbContext context, IPlanningAgentService planningAgent, INotificationAgentService notificationAgent, ILogger<InventoryMonitor> logger)
        {
            _context = context;
            _planningAgent = planningAgent;
            _notificationAgent = notificationAgent;
            _logger = logger;
        }

        public async Task<int> RunInventoryCheckAsync()
        {
            var now = DateTime.UtcNow;
            var hospitals = await _context.Hospitals
                .Where(h => h.IsVerified && !h.IsSuspended)
                .Select(h => new { h.HospitalId, h.Name, h.ExpiryAlertDays })
                .ToListAsync();
            if (hospitals.Count == 0) return 0;

            var hospitalIds = hospitals.Select(h => h.HospitalId).ToList();
            var inventories = await _context.BloodInventories
                .Where(i => hospitalIds.Contains(i.HospitalId))
                .Select(i => new { i.HospitalId, i.BloodGroup, i.UnitsAvailable, i.MinimumThreshold, i.MaximumCapacity })
                .ToListAsync();
            var packets = await _context.BloodPackets
                .Where(p => hospitalIds.Contains(p.HospitalId) && p.Status == BloodPacketStatus.Available && p.ExpiryDate > now)
                .Select(p => new { p.HospitalId, p.BloodGroup, p.ExpiryDate })
                .ToListAsync();
            var openRequests = await _context.BloodRequests
                .Where(r => r.Status == BloodRequestStatus.Approved && r.ExpiryDate > now && r.FulfilledUnits < r.UnitsRequired &&
                            hospitalIds.Contains(r.HospitalId))
                .Select(r => new { r.BloodRequestId, r.HospitalId, r.BloodGroup, Remaining = r.UnitsRequired - r.FulfilledUnits, r.Priority })
                .ToListAsync();

            var stock = inventories.Select(i =>
            {
                var hospital = hospitals.First(h => h.HospitalId == i.HospitalId);
                var groupPackets = packets.Where(p => p.HospitalId == i.HospitalId && p.BloodGroup == i.BloodGroup).ToList();
                return new InventorySnapshot(i.HospitalId, hospital.Name, i.BloodGroup, i.UnitsAvailable, i.MinimumThreshold, i.MaximumCapacity,
                    groupPackets.Count(p => p.ExpiryDate <= now.AddDays(hospital.ExpiryAlertDays)), hospital.ExpiryAlertDays,
                    groupPackets.Count > 0 ? groupPackets.Min(p => p.ExpiryDate) : null);
            }).ToList();

            var plan = await _planningAgent.DispatchPlanAsync(new PlanRequestDto
            {
                EventType = "InventoryCheck",
                Payload = new Dictionary<string, object>
                {
                    ["inventories"] = stock.Select(s => s.ToAgentPayload()).ToList(),
                    ["publicRequests"] = openRequests.Select(r => new Dictionary<string, object>
                    {
                        ["request_id"] = r.BloodRequestId.ToString(),
                        ["hospital_id"] = r.HospitalId.ToString(),
                        ["hospital_name"] = hospitals.First(h => h.HospitalId == r.HospitalId).Name,
                        ["blood_group"] = r.BloodGroup,
                        ["remaining_units"] = r.Remaining,
                        ["priority"] = r.Priority
                    }).ToList()
                }
            });

            var allowedHospitals = hospitalIds.ToHashSet();
            if (plan != null && plan.Success)
            {
                return await _notificationAgent.PersistAgentNotificationsAsync(plan.Notifications, new HashSet<Guid>(), allowedHospitals);
            }

            _logger.LogInformation("Supervisor unavailable for InventoryCheck; sending rule-based inventory alerts.");
            return await _notificationAgent.PersistAgentNotificationsAsync(BuildRuleBasedAlerts(stock), new HashSet<Guid>(), allowedHospitals);
        }

        private static IEnumerable<AgentNotificationDto> BuildRuleBasedAlerts(List<InventorySnapshot> stock)
        {
            foreach (var s in stock)
            {
                if (s.UnitsAvailable < s.MinimumThreshold)
                {
                    var sources = stock.Where(o => o.HospitalId != s.HospitalId && o.BloodGroup == s.BloodGroup && o.UnitsAvailable > o.MinimumThreshold)
                        .OrderByDescending(o => o.UnitsAvailable - o.MinimumThreshold)
                        .Take(3)
                        .Select(o => $"{o.HospitalName} ({o.UnitsAvailable - o.MinimumThreshold} spare)")
                        .ToList();
                    yield return new AgentNotificationDto
                    {
                        RecipientType = "Hospital",
                        RecipientId = s.HospitalId.ToString(),
                        NotificationType = "InventoryShortage",
                        Title = $"{s.BloodGroup} stock below threshold",
                        Message = $"{s.BloodGroup} stock is {s.UnitsAvailable} unit(s), below your threshold of {s.MinimumThreshold}." +
                                  (sources.Count > 0 ? $" Hospitals with spare stock: {string.Join(", ", sources)}. Consider a transfer request." : string.Empty)
                    };
                }

                if (s.ExpiringSoonUnits > 0)
                {
                    var takers = stock.Where(o => o.HospitalId != s.HospitalId && o.BloodGroup == s.BloodGroup && o.UnitsAvailable < o.MinimumThreshold)
                        .Take(3).Select(o => o.HospitalName).ToList();
                    yield return new AgentNotificationDto
                    {
                        RecipientType = "Hospital",
                        RecipientId = s.HospitalId.ToString(),
                        NotificationType = "PacketsExpiringSoon",
                        Title = $"{s.BloodGroup} packets expiring soon",
                        Message = $"{s.ExpiringSoonUnits} {s.BloodGroup} packet(s) expire within {s.ExpiryAlertDays} day(s)." +
                                  (takers.Count > 0 ? $" {string.Join(", ", takers)} are below threshold; consider offering them before expiry." : " Use them first or offer them to another hospital to prevent wastage.")
                    };
                }
            }
        }

        public record InventorySnapshot(Guid HospitalId, string HospitalName, string BloodGroup, int UnitsAvailable, int MinimumThreshold,
            int MaximumCapacity, int ExpiringSoonUnits, int ExpiryAlertDays, DateTime? NextExpiryDate)
        {
            public Dictionary<string, object?> ToAgentPayload() => new()
            {
                ["facility_id"] = HospitalId.ToString(),
                ["facility_name"] = HospitalName,
                ["blood_group"] = BloodGroup,
                ["current_units"] = UnitsAvailable,
                ["minimum_threshold"] = MinimumThreshold,
                ["maximum_capacity"] = MaximumCapacity,
                ["expiring_soon_units"] = ExpiringSoonUnits,
                ["expiry_alert_days"] = ExpiryAlertDays,
                ["next_expiry_date"] = NextExpiryDate?.ToString("yyyy-MM-dd")
            };
        }
    }
}
