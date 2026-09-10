using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Notification;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.Notification
{
    public class NotificationAgentService : INotificationAgentService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<NotificationAgentService> _logger;

        public NotificationAgentService(AppDbContext context, ILogger<NotificationAgentService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<int> NotifyEligibleDonorsAsync(Guid bloodRequestId, string bloodGroup, Guid hospitalId, string priority)
        {
            // Find active users
            var users = await _context.Users
                .Where(u => u.AccountStatus == AccountStatus.Active)
                .ToListAsync();

            var notifications = new List<LifeLink.Entities.Notification>();
            foreach (var user in users)
            {
                notifications.Add(new LifeLink.Entities.Notification
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = user.UserId,
                    HospitalId = hospitalId,
                    Title = $"Blood Donation Alert [{priority.ToUpper()}]",
                    Message = $"A verified blood request for group {bloodGroup} needs your donation.",
                    NotificationType = "EligibleDonorAlert",
                    RecipientRole = "Donor",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (notifications.Any())
            {
                await _context.Notifications.AddRangeAsync(notifications);
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Notified {Count} eligible donors for request {RequestId}", notifications.Count, bloodRequestId);
            return notifications.Count;
        }

        public async Task<int> NotifyUrgentHospitalsAsync(Guid bloodRequestId, string bloodGroup, Guid requestingHospitalId, string priority)
        {
            // Find verified hospitals excluding requesting hospital
            var verifiedHospitals = await _context.Hospitals
                .Where(h => h.IsVerified && h.HospitalId != requestingHospitalId)
                .ToListAsync();

            var notifications = new List<LifeLink.Entities.Notification>();
            foreach (var hospital in verifiedHospitals)
            {
                notifications.Add(new LifeLink.Entities.Notification
                {
                    NotificationId = Guid.NewGuid(),
                    HospitalId = hospital.HospitalId,
                    Title = $"URGENT Hospital Blood Request Alert [{priority.ToUpper()}]",
                    Message = $"Urgent request for blood group {bloodGroup} approved by hospital {requestingHospitalId}.",
                    NotificationType = "UrgentHospitalAlert",
                    RecipientRole = "HospitalStaff",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (notifications.Any())
            {
                await _context.Notifications.AddRangeAsync(notifications);
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Notified {Count} verified hospitals for urgent request {RequestId}", notifications.Count, bloodRequestId);
            return notifications.Count;
        }

        public async Task<int> NotifyAdminsAsync(Guid bloodRequestId, string bloodGroup, Guid hospitalId, string priority)
        {
            // Find admin users
            var adminUsers = await _context.Users
                .Where(u => u.UserRoles.Any(ur => ur.Role.Name == "Admin"))
                .ToListAsync();

            var notifications = new List<LifeLink.Entities.Notification>();
            foreach (var admin in adminUsers)
            {
                notifications.Add(new LifeLink.Entities.Notification
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = admin.UserId,
                    HospitalId = hospitalId,
                    Title = $"URGENT System Admin Alert [{priority.ToUpper()}]",
                    Message = $"Urgent blood request {bloodRequestId} ({bloodGroup}) requires administrative monitoring.",
                    NotificationType = "AdminUrgentAlert",
                    RecipientRole = "Admin",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (notifications.Any())
            {
                await _context.Notifications.AddRangeAsync(notifications);
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Notified {Count} admin users for urgent request {RequestId}", notifications.Count, bloodRequestId);
            return notifications.Count;
        }

        public async Task ProcessRequestApprovalNotificationAsync(Guid bloodRequestId, string bloodGroup, Guid hospitalId, string priority)
        {
            var priorityUpper = (priority ?? "Normal").Trim().ToUpperInvariant();

            _logger.LogInformation("Processing approval notification for Request {RequestId} with priority {Priority}", bloodRequestId, priorityUpper);

            if (priorityUpper == "HIGH" || priorityUpper == "CRITICAL")
            {
                await NotifyEligibleDonorsAsync(bloodRequestId, bloodGroup, hospitalId, priorityUpper);
                await NotifyUrgentHospitalsAsync(bloodRequestId, bloodGroup, hospitalId, priorityUpper);
                await NotifyAdminsAsync(bloodRequestId, bloodGroup, hospitalId, priorityUpper);
            }
            else
            {
                // Normal priority
                await NotifyEligibleDonorsAsync(bloodRequestId, bloodGroup, hospitalId, priorityUpper);
            }
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
    }
}
