using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Notification;
using LifeLink.DTOs.Planning;
using LifeLink.Services.Common;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.Notification
{
    public class NotificationAgentService : INotificationAgentService
    {
        private readonly AppDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NotificationAgentService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public NotificationAgentService(
            AppDbContext context,
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<NotificationAgentService> logger)
        {
            _context = context;
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        /// <summary>
        /// Donors who may receive a donation alert for this request (Critical requests only). Every rule is checked
        /// here and again by the Notification agent: plain donor account; Active, not suspended, not permanently blocked;
        /// a SAVED blood group EXACTLY equal to the request's (users without a saved group are never alerted); 120-day
        /// interval; age 18-60 when known; no active donation process; not the request creator; not already accepted or
        /// rejected for this request. (Accepting with a compatible group is still allowed; only alerts are exact.)
        /// </summary>
        public async Task<List<DonorCandidate>> GetEligibleDonorCandidatesAsync(Guid bloodRequestId, string bloodGroup, Guid? excludeUserId = null)
        {
            var now = DateTime.UtcNow;
            var intervalCutoff = now.AddDays(-DonorEligibility.DonationIntervalDays);

            var nonDonorIds = DonorEligibility.NonDonorUserIds(_context);
            var busyDonorIds = _context.Acceptances
                .Where(a => a.Status == AcceptanceStatus.Accepted || a.Status == AcceptanceStatus.ScreeningPending ||
                            a.Status == AcceptanceStatus.ScreeningCompleted || a.Status == AcceptanceStatus.Verified ||
                            (a.BloodRequestId == bloodRequestId && a.Status != AcceptanceStatus.Cancelled))
                .Select(a => a.DonorUserId);

            var users = await _context.Users
                .Where(u => u.AccountStatus == AccountStatus.Active && !u.IsSuspended && !u.IsPermanentlyBlocked &&
                            u.BloodGroup != null && u.BloodGroup == bloodGroup &&
                            (u.LastDonationDate == null || u.LastDonationDate <= intervalCutoff) &&
                            !nonDonorIds.Contains(u.UserId) &&
                            !busyDonorIds.Contains(u.UserId) &&
                            (excludeUserId == null || u.UserId != excludeUserId))
                .ToListAsync();

            return users
                .Where(u => DonorEligibility.IsAgeEligible(u.DateOfBirth, now))
                .Select(u => new DonorCandidate(u.UserId, $"{u.FirstName} {u.LastName}".Trim(), u.BloodGroup!,
                    string.IsNullOrWhiteSpace(u.Address) ? "Nearby" : u.Address, u.LastDonationDate, u.DateOfBirth))
                .ToList();
        }

        /// <summary>
        /// Approved, non-suspended hospitals other than the requesting one that hold the EXACT blood group (at least one
        /// available, unexpired packet of it).
        /// </summary>
        public Task<List<Guid>> GetAlertHospitalIdsAsync(Guid requestingHospitalId, string bloodGroup)
        {
            var now = DateTime.UtcNow;
            return _context.Hospitals
                .Where(h => h.IsVerified && !h.IsSuspended && h.HospitalId != requestingHospitalId &&
                            _context.BloodPackets.Any(p => p.HospitalId == h.HospitalId && p.BloodGroup == bloodGroup &&
                                                           p.Status == BloodPacketStatus.Available && p.ExpiryDate > now))
                .Select(h => h.HospitalId)
                .ToListAsync();
        }

        /// <summary>
        /// Only Critical requests send alerts (owner's decision D11). Normal and High requests alert nobody: donors find
        /// them in the public list, and the creator, hospital and assigned doctor get their usual status notifications.
        /// </summary>
        public static bool IsAlertPriority(string? priority) =>
            (priority ?? "Normal").Trim().Equals("Critical", StringComparison.OrdinalIgnoreCase);

        public async Task<int> NotifyEligibleDonorsAsync(Guid bloodRequestId, string bloodGroup, Guid hospitalId, string priority)
        {
            var hospital = await _context.Hospitals.FindAsync(hospitalId);
            var creatorId = await _context.BloodRequests.Where(r => r.BloodRequestId == bloodRequestId).Select(r => (Guid?)r.PatientUserId).FirstOrDefaultAsync();
            var candidates = await GetEligibleDonorCandidatesAsync(bloodRequestId, bloodGroup, creatorId);
            if (!candidates.Any()) return 0;

            var notifications = candidates.Select(c => NotificationFactory.ForUser(c.UserId, "Donor", "EligibleDonorAlert",
                $"[{priority.ToUpper()}] Blood Donation Alert ({bloodGroup})",
                $"A verified blood request for group {bloodGroup} at {hospital?.Name ?? "Partner Hospital"} needs your donation.")).ToList();

            await _context.Notifications.AddRangeAsync(notifications);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Notified {Count} eligible donors for request {RequestId}", notifications.Count, bloodRequestId);
            return notifications.Count;
        }

        public async Task<int> NotifyUrgentHospitalsAsync(Guid bloodRequestId, string bloodGroup, Guid requestingHospitalId, string priority)
        {
            var hospitalIds = await GetAlertHospitalIdsAsync(requestingHospitalId, bloodGroup);
            if (!hospitalIds.Any()) return 0;

            var requestingName = await _context.Hospitals.Where(h => h.HospitalId == requestingHospitalId).Select(h => h.Name).FirstOrDefaultAsync() ?? "A partner hospital";
            var notifications = hospitalIds.Select(id => NotificationFactory.ForHospital(id, "UrgentHospitalAlert",
                $"[{priority.ToUpper()}] Urgent Hospital Blood Shortage Notice ({bloodGroup})",
                $"{requestingName} has an urgent approved request for blood group {bloodGroup}, and your hospital holds {bloodGroup}. Consider offering a transfer.")).ToList();

            await _context.Notifications.AddRangeAsync(notifications);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Notified {Count} verified hospitals for urgent request {RequestId}", notifications.Count, bloodRequestId);
            return notifications.Count;
        }

        /// <summary>
        /// Saves alerts composed by an agent. Only recipients the backend itself selected are accepted, so an agent
        /// can never address a user or hospital outside the eligible set.
        /// </summary>
        public async Task<int> PersistAgentNotificationsAsync(IEnumerable<AgentNotificationDto> items, ISet<Guid> allowedUserIds, ISet<Guid> allowedHospitalIds) =>
            (await PersistAgentNotificationsDetailedAsync(items, allowedUserIds, allowedHospitalIds)).Added;

        public async Task<AlertPersistResult> PersistAgentNotificationsDetailedAsync(IEnumerable<AgentNotificationDto> items, ISet<Guid> allowedUserIds, ISet<Guid> allowedHospitalIds)
        {
            var result = new AlertPersistResult();
            var notifications = new List<LifeLink.Entities.Notification>();
            foreach (var n in items)
            {
                if (!Guid.TryParse(n.RecipientId, out var recipientId) || string.IsNullOrWhiteSpace(n.Title)) continue;
                var title = Truncate(n.Title, 200);
                var message = Truncate(n.Message, 2000);

                if (n.RecipientType.Equals("Hospital", StringComparison.OrdinalIgnoreCase) && allowedHospitalIds.Contains(recipientId))
                {
                    var hospitalAlert = NotificationFactory.ForHospital(recipientId, n.NotificationType ?? "UrgentHospitalAlert", title, message);
                    hospitalAlert.DedupeKey = string.IsNullOrWhiteSpace(n.DedupeKey) ? null : Truncate(n.DedupeKey.Trim(), 200);
                    notifications.Add(hospitalAlert);
                }
                else if (n.RecipientType.Equals("Donor", StringComparison.OrdinalIgnoreCase) && allowedUserIds.Contains(recipientId))
                {
                    notifications.Add(NotificationFactory.ForUser(recipientId, "Donor", n.NotificationType ?? "EligibleDonorAlert", title, message));
                }
            }

            foreach (var notification in notifications)
            {
                if (notification.HospitalId.HasValue && await IsDuplicateHospitalAlertAsync(notification))
                {
                    result.SkippedDuplicates++;
                    continue;
                }
                // The same key twice in one batch is also a duplicate
                if (notification.DedupeKey != null && _context.Notifications.Local.Any(x => x != notification && x.HospitalId == notification.HospitalId &&
                        x.DedupeKey == notification.DedupeKey && _context.Entry(x).State == EntityState.Added))
                {
                    result.SkippedDuplicates++;
                    continue;
                }
                await _context.Notifications.AddAsync(notification);
                result.Added++;
                result.AddedByType[notification.NotificationType] = result.AddedByType.GetValueOrDefault(notification.NotificationType) + 1;
            }

            if (result.Added > 0)
            {
                await _context.SaveChangesAsync();
            }
            return result;
        }

        public async Task ProcessRequestApprovalNotificationAsync(Guid bloodRequestId, string bloodGroup, Guid hospitalId, string priority)
        {
            var priorityUpper = (priority ?? "Normal").Trim().ToUpperInvariant();
            if (!IsAlertPriority(priorityUpper))
            {
                _logger.LogInformation("Request {RequestId} is {Priority} priority: no donor or hospital alerts are sent.", bloodRequestId, priorityUpper);
                return;
            }
            var agentBaseUrl = _configuration["NotificationAgent:BaseUrl"] ?? "http://localhost:8000";
            var hospitalName = await _context.Hospitals.Where(h => h.HospitalId == hospitalId).Select(h => h.Name).FirstOrDefaultAsync() ?? "Partner Hospital";
            var creatorId = await _context.BloodRequests.Where(r => r.BloodRequestId == bloodRequestId).Select(r => (Guid?)r.PatientUserId).FirstOrDefaultAsync();

            var candidates = await GetEligibleDonorCandidatesAsync(bloodRequestId, bloodGroup, creatorId);
            var hospitalIds = await GetAlertHospitalIdsAsync(hospitalId, bloodGroup);

            var payload = new
            {
                request_id = bloodRequestId.ToString(),
                blood_group = bloodGroup,
                units_required = 1,
                priority = priority,
                hospital_id = hospitalId.ToString(),
                hospital_name = hospitalName,
                patient_reason = "Approved blood transfusion need",
                available_donors = candidates.Select(c => c.ToAgentPayload()).ToList(),
                verified_hospital_ids = hospitalIds.Select(id => id.ToString()).ToList()
            };

            bool agentSuccess = false;

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                _logger.LogInformation("Calling LangGraph Notification Agent at {Url}/process-request", agentBaseUrl);

                using var agentRequest = new HttpRequestMessage(HttpMethod.Post, $"{agentBaseUrl.TrimEnd('/')}/process-request") { Content = content };
                AddInternalKey(agentRequest);
                var response = await _httpClient.SendAsync(agentRequest);
                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var agentResult = JsonSerializer.Deserialize<AgentProcessResponseDto>(responseBody, _jsonOptions);
                    var items = (agentResult?.Notifications ?? new List<AgentNotificationItemDto>())
                        .Select(n => new AgentNotificationDto
                        {
                            RecipientType = n.RecipientType,
                            RecipientId = n.RecipientId,
                            Title = n.Title,
                            Message = n.Message
                        });
                    await PersistAgentNotificationsAsync(items, candidates.Select(c => c.UserId).ToHashSet(), hospitalIds.ToHashSet());
                    agentSuccess = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LangGraph Agent microservice call failed or unavailable. Falling back to internal notification processor.");
            }

            // Fallback routing if the agent was not reached: exact-group donors and exact-group stock holders
            if (!agentSuccess)
            {
                await NotifyEligibleDonorsAsync(bloodRequestId, bloodGroup, hospitalId, priorityUpper);
                await NotifyUrgentHospitalsAsync(bloodRequestId, bloodGroup, hospitalId, priorityUpper);
            }
        }

        private void AddInternalKey(HttpRequestMessage request)
        {
            var key = _configuration["InternalService:ApiKey"];
            if (!string.IsNullOrWhiteSpace(key))
            {
                request.Headers.Add("X-Internal-Key", key);
            }
        }

        // Scheduled inventory checks run every 30 minutes; the same unread alert is not repeated within 12 hours.
        // Inventory alerts carry a stable DedupeKey (type + blood group [+ low hospital]); other alerts fall back to type + title.
        private Task<bool> IsDuplicateHospitalAlertAsync(LifeLink.Entities.Notification alert)
        {
            var since = DateTime.UtcNow.AddHours(-12);
            var hospitalId = alert.HospitalId;
            var recent = _context.Notifications.Where(n => n.HospitalId == hospitalId && n.UserId == null && !n.IsRead && n.DismissedAt == null &&
                                                           n.CreatedAt >= since);
            return alert.DedupeKey != null
                ? recent.AnyAsync(n => n.DedupeKey == alert.DedupeKey)
                : recent.AnyAsync(n => n.NotificationType == alert.NotificationType && n.Title == alert.Title);
        }

        private static string Truncate(string? value, int max) =>
            string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);

        public async Task<NotificationResponseDto> CreateRecommendationNotificationAsync(CreateRecommendationNotificationDto dto)
        {
            var notificationType = !string.IsNullOrWhiteSpace(dto.RecommendationType)
                ? dto.RecommendationType
                : (!string.IsNullOrWhiteSpace(dto.NotificationType) ? dto.NotificationType : "RECIPIENT");

            var existing = await _context.Notifications
                .Where(n => n.HospitalId == dto.TargetFacilityId && n.UserId == null && !n.IsRead && n.DismissedAt == null &&
                            n.NotificationType == notificationType && n.Title == dto.Title &&
                            n.CreatedAt >= DateTime.UtcNow.AddHours(-12))
                .FirstOrDefaultAsync();

            var notification = existing ?? NotificationFactory.ForHospital(dto.TargetFacilityId, notificationType, dto.Title, dto.Message);
            if (existing == null)
            {
                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Created recommendation notification '{NotificationId}' for hospital '{HospitalId}' ({Type})",
                    notification.NotificationId,
                    notification.HospitalId,
                    notification.NotificationType
                );
            }

            return new NotificationResponseDto
            {
                NotificationId = notification.NotificationId,
                UserId = notification.UserId,
                HospitalId = notification.HospitalId,
                Title = notification.Title,
                Message = notification.Message,
                NotificationType = notification.NotificationType,
                RecipientRole = notification.RecipientRole,
                IsRead = notification.IsRead,
                CreatedAt = notification.CreatedAt
            };
        }

        public async Task<List<NotificationResponseDto>> GetNotificationsForUserAsync(Guid userId)
        {
            return await _context.Notifications
                .Where(n => n.UserId == userId && n.DismissedAt == null)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new NotificationResponseDto
                {
                    NotificationId = n.NotificationId,
                    UserId = n.UserId,
                    HospitalId = n.HospitalId,
                    Title = n.Title,
                    Message = n.Message,
                    NotificationType = n.NotificationType,
                    RecipientRole = n.RecipientRole,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<List<NotificationResponseDto>> GetAllNotificationsAsync()
        {
            return await _context.Notifications
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new NotificationResponseDto
                {
                    NotificationId = n.NotificationId,
                    UserId = n.UserId,
                    HospitalId = n.HospitalId,
                    Title = n.Title,
                    Message = n.Message,
                    NotificationType = n.NotificationType,
                    RecipientRole = n.RecipientRole,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<int> GetUnreadCountAsync(Guid? userId, Guid? hospitalId = null)
        {
            if (!userId.HasValue && !hospitalId.HasValue) return 0;

            var query = _context.Notifications.Where(n => !n.IsRead && n.DismissedAt == null);
            if (userId.HasValue && hospitalId.HasValue)
            {
                query = query.Where(n => n.UserId == userId.Value || n.HospitalId == hospitalId.Value);
            }
            else if (userId.HasValue)
            {
                query = query.Where(n => n.UserId == userId.Value);
            }
            else if (hospitalId.HasValue)
            {
                query = query.Where(n => n.HospitalId == hospitalId.Value);
            }

            return await query.CountAsync();
        }

        public async Task<List<NotificationResponseDto>> GetNotificationsForCallerAsync(Guid? userId, Guid? hospitalId = null)
        {
            if (!userId.HasValue && !hospitalId.HasValue) return new List<NotificationResponseDto>();

            // Dismissed notifications are hidden from their recipient (they stay in the database)
            var query = _context.Notifications.Where(n => n.DismissedAt == null);
            if (userId.HasValue && hospitalId.HasValue)
            {
                query = query.Where(n => n.UserId == userId.Value || n.HospitalId == hospitalId.Value);
            }
            else if (userId.HasValue)
            {
                query = query.Where(n => n.UserId == userId.Value);
            }
            else if (hospitalId.HasValue)
            {
                query = query.Where(n => n.HospitalId == hospitalId.Value);
            }

            return await query
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new NotificationResponseDto
                {
                    NotificationId = n.NotificationId,
                    UserId = n.UserId,
                    HospitalId = n.HospitalId,
                    Title = n.Title,
                    Message = n.Message,
                    NotificationType = n.NotificationType,
                    RecipientRole = n.RecipientRole,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<bool> MarkNotificationReadAsync(Guid notificationId, Guid? userId, Guid? hospitalId = null, bool isAdmin = false)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification == null) return false;

            if (!isAdmin)
            {
                bool matchesUser = userId.HasValue && notification.UserId == userId.Value;
                bool matchesHospital = hospitalId.HasValue && notification.HospitalId == hospitalId.Value;
                if (!matchesUser && !matchesHospital) return false;
            }

            notification.IsRead = true;
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Dismisses one notification addressed to the caller (their user or their hospital). Soft delete: it is hidden
        /// from the caller's lists and counts but stays in the database.
        /// </summary>
        public async Task<bool> DeleteNotificationAsync(Guid notificationId, Guid? userId, Guid? hospitalId = null)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification == null || notification.DismissedAt != null) return false;

            bool matchesUser = userId.HasValue && notification.UserId == userId.Value;
            bool matchesHospital = hospitalId.HasValue && notification.HospitalId == hospitalId.Value;
            if (!matchesUser && !matchesHospital) return false;

            notification.DismissedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> MarkAllNotificationsReadAsync(Guid? userId, Guid? hospitalId = null, bool isAdmin = false)
        {
            var query = _context.Notifications.Where(n => !n.IsRead && n.DismissedAt == null);
            if (!isAdmin)
            {
                if (userId.HasValue && hospitalId.HasValue)
                {
                    query = query.Where(n => n.UserId == userId.Value || n.HospitalId == hospitalId.Value);
                }
                else if (userId.HasValue)
                {
                    query = query.Where(n => n.UserId == userId.Value);
                }
                else if (hospitalId.HasValue)
                {
                    query = query.Where(n => n.HospitalId == hospitalId.Value);
                }
                else
                {
                    return 0;
                }
            }

            var unreadNotifications = await query.ToListAsync();
            foreach (var n in unreadNotifications)
            {
                n.IsRead = true;
            }

            await _context.SaveChangesAsync();
            return unreadNotifications.Count;
        }

        private class AgentProcessResponseDto
        {
            public string RequestId { get; set; } = string.Empty;
            public string Priority { get; set; } = string.Empty;
            public bool IsUrgent { get; set; }
            public int EligibleDonorsCount { get; set; }
            public List<AgentNotificationItemDto> Notifications { get; set; } = new();
        }

        private class AgentNotificationItemDto
        {
            public string RecipientType { get; set; } = string.Empty;
            public string? RecipientId { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string? EmailSubject { get; set; }
            public string? EmailBody { get; set; }
            public string? SmsBody { get; set; }
        }
    }
}
