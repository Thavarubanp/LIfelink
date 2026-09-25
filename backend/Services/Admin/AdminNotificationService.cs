using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using NotificationEntity = LifeLink.Entities.Notification;
using LifeLink.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.Admin
{
    public class AdminNotificationService : IAdminNotificationService
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<AdminNotificationService> _logger;

        public AdminNotificationService(
            AppDbContext context,
            IEmailService emailService,
            ILogger<AdminNotificationService> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task NotifyHospitalApprovedAsync(Hospital hospital)
        {
            var title = "Hospital Registration Approved";
            var message = $"Your hospital '{hospital.Name}' registration has been approved by platform administration. Operational access is now fully enabled.";

            var notification = new NotificationEntity
            {
                NotificationId = Guid.NewGuid(),
                HospitalId = hospital.HospitalId,
                Title = title,
                Message = message,
                NotificationType = "HospitalApproved",
                RecipientRole = "HospitalStaff",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Notifications.AddAsync(notification);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(hospital.Email))
            {
                await _emailService.SendEmailAsync(hospital.Email, title, message);
            }
        }

        public async Task NotifyHospitalRejectedAsync(Hospital hospital, string reason)
        {
            var title = "Hospital Registration Rejected";
            var message = $"Your hospital registration was rejected. Reason: {reason}. Reply or send corrections from your registration status page.";

            var notification = new NotificationEntity
            {
                NotificationId = Guid.NewGuid(),
                HospitalId = hospital.HospitalId,
                Title = title,
                Message = message,
                NotificationType = "HospitalRejected",
                RecipientRole = "HospitalStaff",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Notifications.AddAsync(notification);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(hospital.Email))
            {
                await _emailService.SendEmailAsync(hospital.Email, title, message);
            }
        }

        public async Task NotifyHospitalRegistrationCommentAsync(Hospital hospital, string comment)
        {
            var title = "New Message About Your Hospital Registration";
            var message = $"LifeLink administration commented on your registration: {comment} Reply or send corrections from your registration status page.";

            await _context.Notifications.AddAsync(new NotificationEntity
            {
                NotificationId = Guid.NewGuid(),
                HospitalId = hospital.HospitalId,
                Title = title,
                Message = message,
                NotificationType = "HospitalRegistrationComment",
                RecipientRole = "HospitalStaff",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(hospital.Email))
            {
                await _emailService.SendEmailAsync(hospital.Email, title, message);
            }
        }

        public async Task NotifyUserSuspendedAsync(User user, string reason, DateTime? until)
        {
            var title = "Account Suspended";
            var expiryText = until.HasValue ? until.Value.ToString("u") : "Indefinite";
            var message = $"Your user account has been suspended. Reason: {reason}. Expiry: {expiryText}. You may submit an explanation or appeal via /api/appeals.";

            var notification = new NotificationEntity
            {
                NotificationId = Guid.NewGuid(),
                UserId = user.UserId,
                Title = title,
                Message = message,
                NotificationType = "UserSuspended",
                RecipientRole = "User",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Notifications.AddAsync(notification);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                var emailBody = $"{message}\n\nAppeal Instructions: Authenticate to LifeLink and post your appeal to /api/appeals. If this suspension involves a complaint, you may submit evidence.";
                await _emailService.SendEmailAsync(user.Email, title, emailBody);
            }
        }

        public async Task NotifyHospitalSuspendedAsync(Hospital hospital, string reason, DateTime? until)
        {
            var title = "Hospital Operations Suspended";
            var expiryText = until.HasValue ? until.Value.ToString("u") : "Indefinite";
            var message = $"Hospital '{hospital.Name}' operations have been suspended. Reason: {reason}. Expiry: {expiryText}. You may submit an explanation or appeal via /api/appeals. If requested, submit activity evidence via /api/hospital/activity-reports.";

            var notification = new NotificationEntity
            {
                NotificationId = Guid.NewGuid(),
                HospitalId = hospital.HospitalId,
                Title = title,
                Message = message,
                NotificationType = "HospitalSuspended",
                RecipientRole = "HospitalStaff",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Notifications.AddAsync(notification);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(hospital.Email))
            {
                var emailBody = $"{message}\n\nAppeal Instructions: Post appeal to /api/appeals.\nComplaint Evidence Instructions: If requested by Admin, submit activity reports to /api/hospital/activity-reports.";
                await _emailService.SendEmailAsync(hospital.Email, title, emailBody);
            }
        }

        public async Task NotifyUserReinstatedAsync(User user)
        {
            var title = "Account Reinstated";
            var message = "Your LifeLink account suspension has been lifted by platform administration. Full operational platform access is now restored.";

            var notification = new NotificationEntity
            {
                NotificationId = Guid.NewGuid(),
                UserId = user.UserId,
                Title = title,
                Message = message,
                NotificationType = "UserReinstated",
                RecipientRole = "User",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Notifications.AddAsync(notification);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                await _emailService.SendEmailAsync(user.Email, title, message);
            }
        }

        public async Task NotifyHospitalReinstatedAsync(Hospital hospital)
        {
            var title = "Hospital Operations Reinstated";
            var message = $"Suspension for hospital '{hospital.Name}' has been lifted by platform administration. All inventory and transfer operations are restored.";

            var notification = new NotificationEntity
            {
                NotificationId = Guid.NewGuid(),
                HospitalId = hospital.HospitalId,
                Title = title,
                Message = message,
                NotificationType = "HospitalReinstated",
                RecipientRole = "HospitalStaff",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Notifications.AddAsync(notification);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(hospital.Email))
            {
                await _emailService.SendEmailAsync(hospital.Email, title, message);
            }
        }

        /// <summary>
        /// Notifies the complaint creator (User or HospitalStaff login). Addressed by UserId only, because
        /// Complaint.HospitalId is the hospital complained about, not the creator.
        /// </summary>
        public async Task NotifyComplaintCreatorAsync(Complaint complaint, string title, string message)
        {
            if (!complaint.UserId.HasValue) return;

            var isHospitalStaff = await _context.UserRoles
                .AnyAsync(ur => ur.UserId == complaint.UserId.Value && ur.Role.Name == "HospitalStaff");

            await _context.Notifications.AddAsync(new NotificationEntity
            {
                NotificationId = Guid.NewGuid(),
                UserId = complaint.UserId,
                Title = title,
                Message = message,
                NotificationType = "Complaint",
                RecipientRole = isHospitalStaff ? "HospitalStaff" : "User",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        /// <summary>Notifies the Admin (the single active Admin account).</summary>
        public async Task NotifyAdminAsync(string title, string message)
        {
            var adminIds = await _context.UserRoles.Where(ur => ur.Role.Name == "Admin").Select(ur => ur.UserId).Distinct().ToListAsync();
            foreach (var adminId in adminIds)
            {
                await _context.Notifications.AddAsync(new NotificationEntity
                {
                    NotificationId = Guid.NewGuid(), UserId = adminId, Title = title, Message = message,
                    NotificationType = "Governance", RecipientRole = "Admin", IsRead = false, CreatedAt = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync();
        }

        /// <summary>Notifies a user account, or a hospital's staff when only the hospital is known.</summary>
        public async Task NotifyUserAsync(Guid? userId, Guid? hospitalId, string title, string message)
        {
            if (!userId.HasValue && !hospitalId.HasValue) return;
            await _context.Notifications.AddAsync(new NotificationEntity
            {
                NotificationId = Guid.NewGuid(), UserId = userId, HospitalId = userId.HasValue ? null : hospitalId,
                Title = title, Message = message, NotificationType = "Governance",
                RecipientRole = userId.HasValue ? "User" : "HospitalStaff", IsRead = false, CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Notifies the admin assigned to the complaint (the admin who replied), or every admin when none is assigned yet.
        /// </summary>
        public async Task NotifyComplaintAdminsAsync(Complaint complaint, string title, string message)
        {
            var adminIds = complaint.AssignedAdminId.HasValue
                ? new List<Guid> { complaint.AssignedAdminId.Value }
                : await _context.UserRoles.Where(ur => ur.Role.Name == "Admin").Select(ur => ur.UserId).Distinct().ToListAsync();

            foreach (var adminId in adminIds)
            {
                await _context.Notifications.AddAsync(new NotificationEntity
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = adminId,
                    Title = title,
                    Message = message,
                    NotificationType = "Complaint",
                    RecipientRole = "Admin",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync();
        }

        public async Task NotifyAppealApprovedAsync(Appeal appeal)
        {
            var title = "Appeal Approved - Suspension Reinstated";
            var message = $"Your appeal has been APPROVED. Admin response: {appeal.AdminResponse}. Your suspension has been removed and operational access is fully restored.";

            var notification = new NotificationEntity
            {
                NotificationId = Guid.NewGuid(),
                UserId = appeal.UserId,
                HospitalId = appeal.HospitalId,
                Title = title,
                Message = message,
                NotificationType = "AppealApproved",
                RecipientRole = appeal.UserId.HasValue ? "User" : "HospitalStaff",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Notifications.AddAsync(notification);
            await _context.SaveChangesAsync();

            string? recipientEmail = null;
            if (appeal.UserId.HasValue)
            {
                var user = await _context.Users.FindAsync(appeal.UserId.Value);
                recipientEmail = user?.Email;
            }
            else if (appeal.HospitalId.HasValue)
            {
                var hospital = await _context.Hospitals.FindAsync(appeal.HospitalId.Value);
                recipientEmail = hospital?.Email;
            }

            if (!string.IsNullOrWhiteSpace(recipientEmail))
            {
                await _emailService.SendEmailAsync(recipientEmail, title, message);
            }
        }

        public async Task NotifyAppealRejectedAsync(Appeal appeal)
        {
            var title = "Appeal Decision: Rejected";
            var message = $"Your appeal has been REJECTED. Admin response: {appeal.AdminResponse}. Suspension remains in effect.";

            var notification = new NotificationEntity
            {
                NotificationId = Guid.NewGuid(),
                UserId = appeal.UserId,
                HospitalId = appeal.HospitalId,
                Title = title,
                Message = message,
                NotificationType = "AppealRejected",
                RecipientRole = appeal.UserId.HasValue ? "User" : "HospitalStaff",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Notifications.AddAsync(notification);
            await _context.SaveChangesAsync();

            string? recipientEmail = null;
            if (appeal.UserId.HasValue)
            {
                var user = await _context.Users.FindAsync(appeal.UserId.Value);
                recipientEmail = user?.Email;
            }
            else if (appeal.HospitalId.HasValue)
            {
                var hospital = await _context.Hospitals.FindAsync(appeal.HospitalId.Value);
                recipientEmail = hospital?.Email;
            }

            if (!string.IsNullOrWhiteSpace(recipientEmail))
            {
                await _emailService.SendEmailAsync(recipientEmail, title, message);
            }
        }
    }
}
