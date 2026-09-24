using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Notification;
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

        public async Task<int> NotifyEligibleDonorsAsync(Guid bloodRequestId, string bloodGroup, Guid hospitalId, string priority)
        {
            var hospital = await _context.Hospitals.FindAsync(hospitalId);
            var users = await _context.Users.Where(u => u.AccountStatus == AccountStatus.Active).ToListAsync();

            if (!users.Any()) return 0;

            var notifications = new List<LifeLink.Entities.Notification>();
            foreach (var user in users)
            {
                notifications.Add(new LifeLink.Entities.Notification
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = user.UserId,
                    HospitalId = hospitalId,
                    Title = $"[{priority.ToUpper()}] Blood Donation Alert ({bloodGroup})",
                    Message = $"A verified blood request for group {bloodGroup} at {hospital?.Name ?? "Partner Hospital"} needs your donation.",
                    NotificationType = "EligibleDonorAlert",
                    RecipientRole = "Donor",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.Notifications.AddRangeAsync(notifications);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Notified {Count} eligible donors for request {RequestId}", notifications.Count, bloodRequestId);
            return notifications.Count;
        }

        public async Task<int> NotifyUrgentHospitalsAsync(Guid bloodRequestId, string bloodGroup, Guid requestingHospitalId, string priority)
        {
            var verifiedHospitals = await _context.Hospitals
                .Where(h => h.IsVerified && h.HospitalId != requestingHospitalId)
                .ToListAsync();

            if (!verifiedHospitals.Any()) return 0;

            var notifications = new List<LifeLink.Entities.Notification>();
            foreach (var hospital in verifiedHospitals)
            {
                notifications.Add(new LifeLink.Entities.Notification
                {
                    NotificationId = Guid.NewGuid(),
                    HospitalId = hospital.HospitalId,
                    Title = $"[{priority.ToUpper()}] Urgent Hospital Blood Shortage Notice ({bloodGroup})",
                    Message = $"Urgent request for blood group {bloodGroup} approved by hospital {requestingHospitalId}.",
                    NotificationType = "UrgentHospitalAlert",
                    RecipientRole = "HospitalStaff",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.Notifications.AddRangeAsync(notifications);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Notified {Count} verified hospitals for urgent request {RequestId}", notifications.Count, bloodRequestId);
            return notifications.Count;
        }

        public async Task ProcessRequestApprovalNotificationAsync(Guid bloodRequestId, string bloodGroup, Guid hospitalId, string priority)
        {
            var priorityUpper = (priority ?? "Normal").Trim().ToUpperInvariant();
            var agentBaseUrl = _configuration["NotificationAgent:BaseUrl"] ?? "http://localhost:8000";

            var hospital = await _context.Hospitals.FindAsync(hospitalId);
            var hospitalName = hospital?.Name ?? "Partner Hospital";

            var activeDonors = await _context.Users
                .Where(u => u.AccountStatus == AccountStatus.Active)
                .Select(u => new
                {
                    user_id = u.UserId.ToString(),
                    full_name = $"{u.FirstName} {u.LastName}".Trim(),
                    blood_group = bloodGroup,
                    location = u.Address ?? "Nearby",
                    account_status = "Active"
                })
                .ToListAsync();

            var verifiedHospitalIds = await _context.Hospitals
                .Where(h => h.IsVerified && h.HospitalId != hospitalId)
                .Select(h => h.HospitalId.ToString())
                .ToListAsync();

            var payload = new
            {
                request_id = bloodRequestId.ToString(),
                blood_group = bloodGroup,
                units_required = 1,
                priority = priority,
                hospital_id = hospitalId.ToString(),
                hospital_name = hospitalName,
                patient_reason = "Approved blood transfusion need",
                available_donors = activeDonors,
                verified_hospital_ids = verifiedHospitalIds
            };

            bool agentSuccess = false;

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                _logger.LogInformation("Calling LangGraph Notification Agent at {Url}/process-request", agentBaseUrl);

                var response = await _httpClient.PostAsync($"{agentBaseUrl.TrimEnd('/')}/process-request", content);
                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var agentResult = JsonSerializer.Deserialize<AgentProcessResponseDto>(responseBody, _jsonOptions);

                    if (agentResult?.Notifications != null && agentResult.Notifications.Any())
                    {
                        var dbNotifications = new List<LifeLink.Entities.Notification>();
                        foreach (var n in agentResult.Notifications)
                        {
                            // Strictly filter out any Admin notifications if returned
                            if (n.RecipientType.Equals("Admin", StringComparison.OrdinalIgnoreCase)) continue;

                            Guid? uid = Guid.TryParse(n.RecipientId, out var parsedUid) ? parsedUid : null;
                            Guid? hid = (n.RecipientType == "Hospital" && Guid.TryParse(n.RecipientId, out var parsedHid)) ? parsedHid : hospitalId;

                            dbNotifications.Add(new LifeLink.Entities.Notification
                            {
                                NotificationId = Guid.NewGuid(),
                                UserId = (n.RecipientType != "Hospital") ? uid : null,
                                HospitalId = hid,
                                Title = n.Title,
                                Message = n.Message,
                                NotificationType = n.RecipientType == "Hospital" ? "UrgentHospitalAlert" : "EligibleDonorAlert",
                                RecipientRole = n.RecipientType,
                                IsRead = false,
                                CreatedAt = DateTime.UtcNow
                            });
                        }

                        if (dbNotifications.Any())
                        {
                            await _context.Notifications.AddRangeAsync(dbNotifications);
                            await _context.SaveChangesAsync();
                            agentSuccess = true;
                            _logger.LogInformation("Successfully stored {Count} agent-generated notifications in database.", dbNotifications.Count);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LangGraph Agent microservice call failed or unavailable. Falling back to internal notification processor.");
            }

            // Fallback routing if LangGraph service was not reached (Donors always, Hospitals only on High/Critical)
            if (!agentSuccess)
            {
                if (priorityUpper == "HIGH" || priorityUpper == "CRITICAL")
                {
                    await NotifyEligibleDonorsAsync(bloodRequestId, bloodGroup, hospitalId, priorityUpper);
                    await NotifyUrgentHospitalsAsync(bloodRequestId, bloodGroup, hospitalId, priorityUpper);
                }
                else
                {
                    await NotifyEligibleDonorsAsync(bloodRequestId, bloodGroup, hospitalId, priorityUpper);
                }
            }
        }

        public async Task<NotificationResponseDto> CreateRecommendationNotificationAsync(CreateRecommendationNotificationDto dto)
        {
            var notificationType = !string.IsNullOrWhiteSpace(dto.RecommendationType)
                ? dto.RecommendationType
                : (!string.IsNullOrWhiteSpace(dto.NotificationType) ? dto.NotificationType : "RECIPIENT");

            var notification = new LifeLink.Entities.Notification
            {
                NotificationId = Guid.NewGuid(),
                HospitalId = dto.TargetFacilityId,
                UserId = null,
                Title = dto.Title,
                Message = dto.Message,
                NotificationType = notificationType,
                RecipientRole = "HospitalStaff",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Created recommendation notification '{NotificationId}' for hospital '{HospitalId}' ({Type})",
                notification.NotificationId,
                notification.HospitalId,
                notification.NotificationType
            );

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
                .Where(n => n.UserId == userId)
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

            var query = _context.Notifications.Where(n => !n.IsRead);
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

            var query = _context.Notifications.AsQueryable();
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

        /// <summary>Deletes one notification addressed to the caller (their user or their hospital).</summary>
        public async Task<bool> DeleteNotificationAsync(Guid notificationId, Guid? userId, Guid? hospitalId = null)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification == null) return false;

            bool matchesUser = userId.HasValue && notification.UserId == userId.Value;
            bool matchesHospital = hospitalId.HasValue && notification.HospitalId == hospitalId.Value;
            if (!matchesUser && !matchesHospital) return false;

            _context.Notifications.Remove(notification);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> MarkAllNotificationsReadAsync(Guid? userId, Guid? hospitalId = null, bool isAdmin = false)
        {
            var query = _context.Notifications.Where(n => !n.IsRead);
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
