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
    /// Threshold and expiry monitoring (scheduled, or started by hospital staff through InventoryAnalysisService).
    /// The backend gathers each approved hospital's stock, expiring packets and open public requests, and the Supervisor
    /// runs the Inventory agent, the planning step and the Notification agent (wording). Without the Supervisor the same
    /// rules run here (rule-based alerts). Alerts are saved only for approved, non-suspended hospitals; the same unread
    /// alert (same DedupeKey) is not repeated within 12 hours.
    ///
    /// Rules (owner's Phase 4 decision, exact blood group only):
    /// - A group is LOW when UnitsAvailable &lt; MinimumThreshold (InventoryRules). The low hospital gets an
    ///   "InventoryShortage" alert listing the other hospitals holding that exact group above their own threshold.
    /// - Every other hospital whose stock of that exact group is ABOVE its own threshold gets an "InventoryShortageHelp"
    ///   alert. Hospitals at or below their threshold are not asked to help.
    /// - Expiring-soon alerts stay, matched to hospitals low on the exact group and open public requests for the exact group.
    /// - No surplus alerts. Alert text shows unit counts only, never threshold figures.
    /// </summary>
    public class InventoryMonitor
    {
        public const string ShortageType = "InventoryShortage";
        public const string ShortageHelpType = "InventoryShortageHelp";
        public const string ExpiringType = "PacketsExpiringSoon";

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

        /// <summary>Result of one check: alerts saved per kind, duplicates skipped, and whether the agents were used.</summary>
        public record InventoryCheckResult(int LowStockAlerts, int ExpiringAlerts, int SkippedDuplicates, bool UsedAgents)
        {
            public int Total => LowStockAlerts + ExpiringAlerts;
        }

        public async Task<InventoryCheckResult> RunInventoryCheckAsync()
        {
            var now = DateTime.UtcNow;
            var hospitals = await _context.Hospitals
                .Where(h => h.IsVerified && !h.IsSuspended)
                .Select(h => new { h.HospitalId, h.Name, h.ExpiryAlertDays })
                .ToListAsync();
            if (hospitals.Count == 0) return new InventoryCheckResult(0, 0, 0, false);

            var hospitalIds = hospitals.Select(h => h.HospitalId).ToList();
            var inventories = await _context.BloodInventories
                .Where(i => hospitalIds.Contains(i.HospitalId) && i.DeletedAt == null)
                .Select(i => new { i.HospitalId, i.BloodGroup, i.UnitsAvailable, i.MinimumThreshold, i.MaximumCapacity })
                .ToListAsync();
            var packets = await _context.BloodPackets
                .Where(p => hospitalIds.Contains(p.HospitalId) && p.Status == BloodPacketStatus.Available && p.ExpiryDate > now)
                .Select(p => new { p.HospitalId, p.BloodGroup, p.ExpiryDate })
                .ToListAsync();
            var openRequests = await _context.BloodRequests
                .Where(r => r.Status == BloodRequestStatus.Approved && r.FulfilledUnits < r.UnitsRequired &&
                            r.AdminSuspendedAt == null && hospitalIds.Contains(r.HospitalId))
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
            var requests = openRequests.Select(r => new OpenRequest(r.BloodRequestId, r.HospitalId,
                hospitals.First(h => h.HospitalId == r.HospitalId).Name, r.BloodGroup, r.Remaining, r.Priority)).ToList();

            var plan = await _planningAgent.DispatchPlanAsync(new PlanRequestDto
            {
                EventType = "InventoryCheck",
                Payload = new Dictionary<string, object>
                {
                    ["inventories"] = stock.Select(s => s.ToAgentPayload()).ToList(),
                    ["publicRequests"] = requests.Select(r => new Dictionary<string, object>
                    {
                        ["request_id"] = r.RequestId.ToString(),
                        ["hospital_id"] = r.HospitalId.ToString(),
                        ["hospital_name"] = r.HospitalName,
                        ["blood_group"] = r.BloodGroup,
                        ["remaining_units"] = r.Remaining,
                        ["priority"] = r.Priority
                    }).ToList()
                }
            });

            var allowedHospitals = hospitalIds.ToHashSet();
            var usedAgents = plan != null && plan.Success;
            if (!usedAgents)
            {
                _logger.LogInformation("Supervisor unavailable for InventoryCheck; sending rule-based inventory alerts.");
            }
            var alerts = usedAgents ? plan!.Notifications : BuildRuleBasedAlerts(stock, requests).ToList();
            var saved = await _notificationAgent.PersistAgentNotificationsDetailedAsync(alerts, new HashSet<Guid>(), allowedHospitals);
            return new InventoryCheckResult(saved.AddedOf(ShortageType, ShortageHelpType), saved.AddedOf(ExpiringType), saved.SkippedDuplicates, usedAgents);
        }

        public static string ShortageKey(string group) => $"{ShortageType}:{group}";
        public static string HelpKey(string group, Guid lowHospitalId) => $"{ShortageHelpType}:{group}:{lowHospitalId}";
        public static string ExpiringKey(string group) => $"{ExpiringType}:{group}";

        private static string Units(int n) => n == 1 ? "1 unit" : $"{n} units";

        /// <summary>The same rules as the Inventory agent, used when the Supervisor cannot be reached.</summary>
        public static IEnumerable<AgentNotificationDto> BuildRuleBasedAlerts(List<InventorySnapshot> stock, List<OpenRequest> requests)
        {
            foreach (var s in stock)
            {
                if (InventoryRules.IsBelowThreshold(s.UnitsAvailable, s.MinimumThreshold))
                {
                    var holders = stock.Where(o => o.HospitalId != s.HospitalId && o.BloodGroup == s.BloodGroup &&
                                                   InventoryRules.IsAboveThreshold(o.UnitsAvailable, o.MinimumThreshold))
                        .OrderByDescending(o => o.UnitsAvailable)
                        .ToList();
                    yield return new AgentNotificationDto
                    {
                        RecipientType = "Hospital",
                        RecipientId = s.HospitalId.ToString(),
                        NotificationType = ShortageType,
                        DedupeKey = ShortageKey(s.BloodGroup),
                        Title = $"Low {s.BloodGroup} stock",
                        Message = $"You have only {Units(s.UnitsAvailable)} of {s.BloodGroup} left." +
                                  (holders.Count > 0
                                      ? $" Hospitals holding {s.BloodGroup}: {string.Join(", ", holders.Take(5).Select(o => $"{o.HospitalName} ({Units(o.UnitsAvailable)})"))}. Consider a transfer request."
                                      : $" No other hospital currently holds extra {s.BloodGroup}; consider a donor blood request.")
                    };
                    foreach (var holder in holders)
                    {
                        yield return new AgentNotificationDto
                        {
                            RecipientType = "Hospital",
                            RecipientId = holder.HospitalId.ToString(),
                            NotificationType = ShortageHelpType,
                            DedupeKey = HelpKey(s.BloodGroup, s.HospitalId),
                            Title = $"{s.HospitalName} needs {s.BloodGroup}",
                            Message = $"{s.HospitalName} has only {Units(s.UnitsAvailable)} of {s.BloodGroup} left. You hold {Units(holder.UnitsAvailable)} of {s.BloodGroup}. Consider offering a transfer."
                        };
                    }
                }

                if (s.ExpiringSoonUnits > 0)
                {
                    var takers = stock.Where(o => o.HospitalId != s.HospitalId && o.BloodGroup == s.BloodGroup &&
                                                  InventoryRules.IsBelowThreshold(o.UnitsAvailable, o.MinimumThreshold))
                        .Take(3).Select(o => $"{o.HospitalName} ({Units(o.UnitsAvailable)} left)");
                    var wanting = requests.Where(r => r.BloodGroup == s.BloodGroup && r.HospitalId != s.HospitalId)
                        .Take(3).Select(r => $"{r.HospitalName} request for {s.BloodGroup} ({Units(r.Remaining)} needed)");
                    var related = takers.Concat(wanting).ToList();
                    yield return new AgentNotificationDto
                    {
                        RecipientType = "Hospital",
                        RecipientId = s.HospitalId.ToString(),
                        NotificationType = ExpiringType,
                        DedupeKey = ExpiringKey(s.BloodGroup),
                        Title = $"{s.BloodGroup} packets expiring soon",
                        Message = $"{s.ExpiringSoonUnits} {s.BloodGroup} packet(s) expire within {s.ExpiryAlertDays} day(s)." +
                                  (related.Count > 0 ? $" Could be used by: {string.Join("; ", related)}. Consider offering them before they expire." : " Use them first to prevent wastage.")
                    };
                }
            }
        }

        public record OpenRequest(Guid RequestId, Guid HospitalId, string HospitalName, string BloodGroup, int Remaining, string Priority);

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
