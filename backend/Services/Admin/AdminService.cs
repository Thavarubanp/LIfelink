using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Admin;
using LifeLink.Entities;
using LifeLink.Services.Hospitals;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Admin
{
    public class AdminService : IAdminService
    {
        private readonly AppDbContext _context;
        private readonly IAdminNotificationService _notificationService;

        public AdminService(AppDbContext context, IAdminNotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        public async Task<AdminDashboardStatsDto> GetDashboardStatsAsync()
        {
            var totalUsers = await _context.Users.CountAsync();
            var totalHospitals = await _context.Hospitals.CountAsync();
            var totalDoctors = await _context.Doctors.CountAsync();

            var activeRequests = await _context.BloodRequests.CountAsync(r =>
                r.Status == BloodRequestStatus.Pending || r.Status == BloodRequestStatus.Verified || r.Status == BloodRequestStatus.Approved);

            var pendingComplaints = await _context.Complaints.CountAsync(c =>
                c.Status == ComplaintStatus.OPEN ||
                c.Status == ComplaintStatus.UNDER_REVIEW ||
                c.Status == ComplaintStatus.AWAITING_INFORMATION);

            // Registrations waiting for the admin: pending, or rejected with a hospital reply as the latest entry
            var pendingHospitalApprovals = await _context.Hospitals.CountAsync(RegistrationThread.NeedsAdminReview);

            var pendingAppeals = await _context.Appeals.CountAsync(a => a.Status == AppealStatus.PENDING);
            var activeSuspendedUsers = await _context.Users.CountAsync(u => u.IsSuspended);
            var activeSuspendedHospitals = await _context.Hospitals.CountAsync(h => h.IsSuspended);

            return new AdminDashboardStatsDto
            {
                TotalUsers = totalUsers,
                TotalHospitals = totalHospitals,
                TotalDoctors = totalDoctors,
                ActiveRequests = activeRequests,
                PendingComplaints = pendingComplaints,
                PendingHospitalApprovals = pendingHospitalApprovals,
                PendingAppeals = pendingAppeals,
                ActiveSuspendedUsers = activeSuspendedUsers,
                ActiveSuspendedHospitals = activeSuspendedHospitals
            };
        }

        /// <summary>Registrations waiting for the admin: pending, or rejected with a hospital reply as the latest entry.</summary>
        public async Task<List<AdminHospitalResponseDto>> GetPendingHospitalsAsync()
        {
            var list = await _context.Hospitals
                .Include(h => h.ApprovalHistories).ThenInclude(e => e.Admin)
                .Where(RegistrationThread.NeedsAdminReview)
                .OrderByDescending(h => h.UpdatedAt)
                .ToListAsync();

            return list.Select(MapToHospitalDto).ToList();
        }

        /// <summary>Approves a pending or rejected registration. Approved registrations are read-only.</summary>
        public async Task<AdminHospitalResponseDto> ApproveHospitalAsync(Guid hospitalId, Guid adminId, Guid? lastSeenEntryId = null)
        {
            var hospital = await LoadHospitalWithConversationAsync(hospitalId);

            if (hospital.ApprovalStatus == ApprovalStatus.Approved)
            {
                throw new ConflictException("This hospital registration is already approved.");
            }
            RegistrationThread.EnsureNoUnseenHospitalReply(hospital, lastSeenEntryId);

            hospital.ApprovalStatus = ApprovalStatus.Approved;
            hospital.IsVerified = true; // Automatic synchronization
            hospital.ApprovedAt = DateTime.UtcNow;
            hospital.ApprovedByAdminId = adminId;
            hospital.RejectionReason = null;
            hospital.RejectionReportUrl = null;
            hospital.RejectionReportName = null;
            hospital.UpdatedAt = DateTime.UtcNow;

            await _context.HospitalApprovalHistories.AddAsync(new HospitalApprovalHistory
            {
                HospitalId = hospital.HospitalId,
                Status = RegistrationEntryType.Approved,
                Timestamp = DateTime.UtcNow,
                AdminId = adminId,
                AdminName = await AdminEmailAsync(adminId),
                Comments = "Hospital registration verified and approved."
            });

            await _context.SaveChangesAsync();

            await _notificationService.NotifyHospitalApprovedAsync(hospital);

            return MapToHospitalDto(hospital);
        }

        /// <summary>
        /// Rejects a registration that is waiting for an admin decision: a new registration (Pending) or a hospital reply
        /// (AwaitingAdminReview). It then waits for the hospital (Rejected) and cannot be rejected again until the
        /// hospital has replied.
        /// </summary>
        public async Task<AdminHospitalResponseDto> RejectHospitalAsync(Guid hospitalId, Guid adminId, RejectHospitalDto dto, Guid? lastSeenEntryId = null)
        {
            var hospital = await LoadHospitalWithConversationAsync(hospitalId);

            if (hospital.ApprovalStatus is not (ApprovalStatus.Pending or ApprovalStatus.AwaitingAdminReview))
            {
                throw new ConflictException(hospital.ApprovalStatus == ApprovalStatus.Approved
                    ? "Approved registrations are read-only."
                    : "This registration is already rejected and waiting for the hospital. Add a comment to continue the conversation.");
            }
            RegistrationThread.EnsureNoUnseenHospitalReply(hospital, lastSeenEntryId);
            AttachmentRules.EnsureValidIfPresent(dto.ReportDocumentUrl);
            var hasReport = AttachmentRules.HasContent(dto.ReportDocumentUrl);
            var reportName = hasReport ? (string.IsNullOrWhiteSpace(dto.ReportDocumentName) ? "Review_Report" : dto.ReportDocumentName.Trim()) : null;

            hospital.ApprovalStatus = ApprovalStatus.Rejected;
            hospital.IsVerified = false; // Automatic synchronization
            hospital.RejectionReason = dto.Reason;
            hospital.RejectionReportName = reportName;
            hospital.RejectionReportUrl = hasReport ? dto.ReportDocumentUrl : null;
            hospital.UpdatedAt = DateTime.UtcNow;

            await _context.HospitalApprovalHistories.AddAsync(new HospitalApprovalHistory
            {
                HospitalId = hospital.HospitalId,
                Status = RegistrationEntryType.Rejected,
                Timestamp = DateTime.UtcNow,
                AdminId = adminId,
                AdminName = await AdminEmailAsync(adminId),
                Comments = dto.Reason,
                ReportDocumentName = reportName,
                ReportDocumentUrl = hasReport ? dto.ReportDocumentUrl : null
            });

            await _context.SaveChangesAsync();

            await _notificationService.NotifyHospitalRejectedAsync(hospital, dto.Reason);

            return MapToHospitalDto(hospital);
        }

        /// <summary>
        /// Admin comment in a rejected registration's conversation. Answering a hospital reply (AwaitingAdminReview) asks
        /// for more information and hands the turn back to the hospital (Rejected); while already waiting for the
        /// hospital, a comment adds to the request and the status stays Rejected.
        /// </summary>
        public async Task<AdminHospitalResponseDto> CommentOnHospitalRegistrationAsync(Guid hospitalId, Guid adminId, HospitalRegistrationCommentDto dto)
        {
            var hospital = await LoadHospitalWithConversationAsync(hospitalId);

            if (hospital.ApprovalStatus is not (ApprovalStatus.Rejected or ApprovalStatus.AwaitingAdminReview))
            {
                throw new ConflictException(hospital.ApprovalStatus == ApprovalStatus.Approved
                    ? "Approved registrations are read-only."
                    : "Comments are available once the registration has been rejected. Approve or reject the pending registration first.");
            }
            RegistrationThread.EnsureNoUnseenHospitalReply(hospital, dto.LastSeenEntryId);

            var message = dto.Message?.Trim() ?? string.Empty;
            if (message.Length < 3)
            {
                throw new InvalidOperationException("A comment of at least 3 characters is required.");
            }
            AttachmentRules.EnsureValidIfPresent(dto.AttachmentUrl);
            var hasAttachment = AttachmentRules.HasContent(dto.AttachmentUrl);

            await _context.HospitalApprovalHistories.AddAsync(new HospitalApprovalHistory
            {
                HospitalId = hospital.HospitalId,
                Status = RegistrationEntryType.AdminComment,
                Timestamp = DateTime.UtcNow,
                AdminId = adminId,
                AdminName = await AdminEmailAsync(adminId),
                Comments = message,
                ReportDocumentName = hasAttachment ? (string.IsNullOrWhiteSpace(dto.AttachmentName) ? "attachment" : dto.AttachmentName.Trim()) : null,
                ReportDocumentUrl = hasAttachment ? dto.AttachmentUrl : null
            });
            hospital.ApprovalStatus = ApprovalStatus.Rejected; // the hospital's turn again
            hospital.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _notificationService.NotifyHospitalRegistrationCommentAsync(hospital, message);

            return MapToHospitalDto(hospital);
        }

        private async Task<Hospital> LoadHospitalWithConversationAsync(Guid hospitalId) =>
            await _context.Hospitals
                .Include(h => h.ApprovalHistories).ThenInclude(e => e.Admin)
                .FirstOrDefaultAsync(h => h.HospitalId == hospitalId)
            ?? throw new KeyNotFoundException($"Hospital with ID {hospitalId} was not found.");

        private Task<string?> AdminEmailAsync(Guid adminId) =>
            _context.Users.Where(u => u.UserId == adminId).Select(u => (string?)u.Email).FirstOrDefaultAsync();

        public async Task<AdminUserResponseDto> SuspendUserAsync(Guid userId, SuspendUserDto dto, Guid? actingAdminId = null)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                throw new KeyNotFoundException($"User with ID {userId} was not found.");
            }

            // Doctor lifecycle is owned by the hospital that created the doctor, not by admins
            var isDoctor = await _context.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.Role.Name == "Doctor");
            if (isDoctor)
            {
                throw new InvalidOperationException("Doctor accounts are managed by their hospital and cannot be suspended by an admin.");
            }
            await EnsureGovernableUserAsync(user, actingAdminId, "suspended");

            user.IsSuspended = true;
            user.SuspendedUntil = dto.SuspendedUntil;
            user.SuspensionReason = dto.Reason;
            user.AccountStatus = AccountStatus.Suspended;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _notificationService.NotifyUserSuspendedAsync(user, dto.Reason, dto.SuspendedUntil);

            return MapToUserDto(user);
        }

        public async Task<AdminUserResponseDto> ReinstateUserAsync(Guid userId, Guid? actingAdminId = null)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                throw new KeyNotFoundException($"User with ID {userId} was not found.");
            }
            if (actingAdminId.HasValue && actingAdminId.Value == userId)
            {
                throw new InvalidOperationException("You cannot perform governance actions on your own account.");
            }

            user.IsSuspended = false;
            user.SuspendedUntil = null;
            user.SuspensionReason = null;
            user.AccountStatus = AccountStatus.Active;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _notificationService.NotifyUserReinstatedAsync(user);

            return MapToUserDto(user);
        }

        public async Task<AdminHospitalResponseDto> SuspendHospitalAsync(Guid hospitalId, SuspendHospitalDto dto)
        {
            var hospital = await _context.Hospitals.FindAsync(hospitalId);
            if (hospital == null)
            {
                throw new KeyNotFoundException($"Hospital with ID {hospitalId} was not found.");
            }

            hospital.IsSuspended = true;
            hospital.SuspendedUntil = dto.SuspendedUntil;
            hospital.SuspensionReason = dto.Reason;
            hospital.UpdatedAt = DateTime.UtcNow;

            // Tell patients with active requests at this hospital to re-create them through another active hospital
            var hospitalEmail = (hospital.Email ?? string.Empty).ToLower();
            var patientIds = await _context.BloodRequests
                .Where(r => r.HospitalId == hospitalId &&
                            (r.Status == BloodRequestStatus.Pending || r.Status == BloodRequestStatus.Verified || r.Status == BloodRequestStatus.Approved))
                .Select(r => r.PatientUserId)
                .Distinct()
                .Where(id => !_context.Users.Any(u => u.UserId == id && u.Email.ToLower() == hospitalEmail)) // skip the hospital's own requests
                .ToListAsync();
            foreach (var patientId in patientIds)
            {
                await _context.Notifications.AddAsync(new LifeLink.Entities.Notification
                {
                    NotificationId = Guid.NewGuid(),
                    UserId = patientId,
                    Title = "Hospital Suspended",
                    Message = $"'{hospital.Name}' has been suspended, so your active blood request there cannot proceed. Please delete that request and create a new request through another active hospital.",
                    NotificationType = "RequestHospitalSuspended",
                    RecipientRole = "Donor",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            await _notificationService.NotifyHospitalSuspendedAsync(hospital, dto.Reason, dto.SuspendedUntil);

            return MapToHospitalDto(hospital);
        }

        public async Task<AdminHospitalResponseDto> ReinstateHospitalAsync(Guid hospitalId)
        {
            var hospital = await _context.Hospitals.FindAsync(hospitalId);
            if (hospital == null)
            {
                throw new KeyNotFoundException($"Hospital with ID {hospitalId} was not found.");
            }

            hospital.IsSuspended = false;
            hospital.SuspendedUntil = null;
            hospital.SuspensionReason = null;
            hospital.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _notificationService.NotifyHospitalReinstatedAsync(hospital);

            return MapToHospitalDto(hospital);
        }

        public async Task<List<AdminUserResponseDto>> GetUsersAsync()
        {
            // Deleted accounts no longer exist for operations; blocked accounts stay listed with their status
            var users = await _context.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .Where(u => u.AccountStatus != AccountStatus.Deleted)
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            return users.Select(MapToUserDto).ToList();
        }

        /// <summary>Permanently blocks a donor/patient account (never Admin, HospitalStaff or Doctor accounts).</summary>
        public async Task<AdminUserResponseDto> BlockUserAsync(Guid userId, Guid actingAdminId)
        {
            var user = await _context.Users.FindAsync(userId)
                ?? throw new KeyNotFoundException($"User with ID {userId} was not found.");
            await EnsureGovernableUserAsync(user, actingAdminId, "permanently blocked");
            await LifeLink.Services.Common.AccountLifecycleHelper.EnsureDonorPatientAccountAsync(_context, userId, "permanently blocked");

            await LifeLink.Services.Common.AccountLifecycleHelper.PermanentlyBlockAsync(_context, user);
            await _context.SaveChangesAsync();
            return MapToUserDto(user);
        }

        /// <summary>
        /// Transfers Admin ownership: the selected active donor/patient becomes the Admin and the acting Admin becomes a
        /// normal User, in one transaction. The unique index on the Admin role guarantees there is never a second Admin.
        /// The former Admin's session ends on its next request (JWT validation), and the new Admin is notified.
        /// </summary>
        public async Task PromoteToAdminAsync(Guid userId, Guid actingAdminId)
        {
            if (userId == actingAdminId)
            {
                throw new InvalidOperationException("You cannot promote your own account.");
            }

            var target = await _context.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.UserId == userId)
                ?? throw new KeyNotFoundException($"User with ID {userId} was not found.");
            var targetRoles = target.UserRoles.Select(ur => ur.Role.Name).ToList();
            var eligible = target.AccountStatus == AccountStatus.Active && !target.IsSuspended && !target.IsPermanentlyBlocked
                && targetRoles.All(r => r == "User");
            if (!eligible)
            {
                throw new InvalidOperationException("Only an active donor/patient account can be promoted to Admin.");
            }

            var adminRole = await _context.Roles.FirstAsync(r => r.Name == "Admin");
            var userRole = await _context.Roles.FirstAsync(r => r.Name == "User");
            var currentAdmin = await _context.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == actingAdminId && ur.RoleId == adminRole.RoleId)
                ?? throw new InvalidOperationException("Only the current Admin can transfer Admin ownership.");

            var useTransaction = _context.Database.IsRelational();
            await using var transaction = useTransaction ? await _context.Database.BeginTransactionAsync() : null;

            // 1. Current Admin -> User (saved first so the single-admin index never sees two Admins)
            _context.UserRoles.Remove(currentAdmin);
            if (!await _context.UserRoles.AnyAsync(ur => ur.UserId == actingAdminId && ur.RoleId == userRole.RoleId))
            {
                await _context.UserRoles.AddAsync(new UserRole { UserId = actingAdminId, RoleId = userRole.RoleId });
            }
            await _context.SaveChangesAsync();

            // 2. Selected User -> Admin, plus the in-app notification
            _context.UserRoles.RemoveRange(target.UserRoles.Where(ur => ur.RoleId == userRole.RoleId));
            await _context.UserRoles.AddAsync(new UserRole { UserId = userId, RoleId = adminRole.RoleId });
            await _context.Notifications.AddAsync(new LifeLink.Entities.Notification
            {
                NotificationId = Guid.NewGuid(),
                UserId = userId,
                Title = "You Are Now the System Administrator",
                Message = "Admin ownership of LifeLink has been transferred to your account. Sign in again to access the Admin portal.",
                NotificationType = "AdminTransfer",
                RecipientRole = "Admin",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            if (transaction != null) await transaction.CommitAsync();
        }

        // Admins never act on themselves or on the Admin account; blocked/deleted accounts are out of governance
        private async Task EnsureGovernableUserAsync(User user, Guid? actingAdminId, string action)
        {
            if (actingAdminId.HasValue && actingAdminId.Value == user.UserId)
            {
                throw new InvalidOperationException("You cannot perform governance actions on your own account.");
            }
            if (await _context.UserRoles.AnyAsync(ur => ur.UserId == user.UserId && ur.Role.Name == "Admin"))
            {
                throw new InvalidOperationException($"The Admin account cannot be {action}.");
            }
            if (LifeLink.Services.Common.AccountLifecycleHelper.IsRemoved(user))
            {
                throw new InvalidOperationException("This account is no longer active.");
            }
        }

        public async Task<List<AdminHospitalResponseDto>> GetAllHospitalsAsync()
        {
            var hospitals = await _context.Hospitals
                .Include(h => h.ApprovalHistories).ThenInclude(e => e.Admin)
                .OrderByDescending(h => h.CreatedAt)
                .ToListAsync();

            return hospitals.Select(MapToHospitalDto).ToList();
        }

        private static AdminHospitalResponseDto MapToHospitalDto(Hospital hospital)
        {
            var hasLicense = AttachmentRules.HasContent(hospital.LicenseDocumentUrl);
            var hasAccreditation = AttachmentRules.HasContent(hospital.AccreditationDocumentUrl);
            var hasRejectionReport = AttachmentRules.HasContent(hospital.RejectionReportUrl);
            return new AdminHospitalResponseDto
            {
                HospitalId = hospital.HospitalId,
                Name = hospital.Name,
                LicenseNumber = hospital.LicenseNumber,
                Address = hospital.Address,
                ContactNumber = hospital.ContactNumber,
                Email = hospital.Email,
                IsVerified = hospital.IsVerified,
                ApprovalStatus = hospital.ApprovalStatus.ToString(),
                AwaitingAdminReview = RegistrationThread.IsAwaitingAdminReview(hospital),
                ApprovedAt = hospital.ApprovedAt,
                ApprovedByAdminId = hospital.ApprovedByAdminId,
                RejectionReason = hospital.RejectionReason,
                RejectionReportUrl = hasRejectionReport ? hospital.RejectionReportUrl : null,
                RejectionReportName = hasRejectionReport ? hospital.RejectionReportName : null,
                RegistrationNumber = hospital.RegistrationNumber,
                City = hospital.City,
                ContactPersonName = hospital.ContactPersonName,
                ContactPersonPhone = hospital.ContactPersonPhone,
                ContactPersonEmail = hospital.ContactPersonEmail,
                LicenseDocumentUrl = hasLicense ? hospital.LicenseDocumentUrl : null,
                LicenseDocumentName = hasLicense ? hospital.LicenseDocumentName : null,
                AccreditationDocumentUrl = hasAccreditation ? hospital.AccreditationDocumentUrl : null,
                AccreditationDocumentName = hasAccreditation ? hospital.AccreditationDocumentName : null,
                IsSuspended = hospital.IsSuspended,
                SuspendedUntil = hospital.SuspendedUntil,
                SuspensionReason = hospital.SuspensionReason,
                CreatedAt = hospital.CreatedAt,
                UpdatedAt = hospital.UpdatedAt,
                ApprovalHistory = RegistrationThread.ToDtos(hospital, includeAdminIdentity: true)
            };
        }

        private static AdminUserResponseDto MapToUserDto(User user)
        {
            return new AdminUserResponseDto
            {
                UserId = user.UserId,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                AccountStatus = user.AccountStatus.ToString(),
                IsSuspended = user.IsSuspended,
                SuspendedUntil = user.SuspendedUntil,
                SuspensionReason = user.SuspensionReason,
                IsPermanentlyBlocked = user.AccountStatus == AccountStatus.Blocked,
                Roles = user.UserRoles.Select(ur => ur.Role?.Name ?? string.Empty).Where(r => r.Length > 0).ToList(),
                CreatedAt = user.CreatedAt
            };
        }
    }
}
