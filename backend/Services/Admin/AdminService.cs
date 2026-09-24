using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Admin;
using LifeLink.Entities;
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

            var pendingHospitalApprovals = await _context.Hospitals.CountAsync(h =>
                h.ApprovalStatus == ApprovalStatus.Pending || !h.IsVerified);

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

        public async Task<List<AdminHospitalResponseDto>> GetPendingHospitalsAsync()
        {
            var list = await _context.Hospitals
                .Include(h => h.ApprovalHistories)
                .Where(h => h.ApprovalStatus == ApprovalStatus.Pending || h.ApprovalStatus == ApprovalStatus.Resubmitted || !h.IsVerified)
                .OrderByDescending(h => h.UpdatedAt)
                .ToListAsync();

            return list.Select(MapToHospitalDto).ToList();
        }

        public async Task<AdminHospitalResponseDto> ApproveHospitalAsync(Guid hospitalId, Guid adminId)
        {
            var hospital = await _context.Hospitals
                .Include(h => h.ApprovalHistories)
                .FirstOrDefaultAsync(h => h.HospitalId == hospitalId);
            if (hospital == null)
            {
                throw new KeyNotFoundException($"Hospital with ID {hospitalId} was not found.");
            }

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
                Status = ApprovalStatus.Approved,
                Timestamp = DateTime.UtcNow,
                AdminId = adminId,
                Comments = "Hospital registration verified and approved."
            });

            await _context.SaveChangesAsync();

            await _notificationService.NotifyHospitalApprovedAsync(hospital);

            return MapToHospitalDto(hospital);
        }

        public async Task<AdminHospitalResponseDto> RejectHospitalAsync(Guid hospitalId, Guid adminId, RejectHospitalDto dto)
        {
            var hospital = await _context.Hospitals
                .Include(h => h.ApprovalHistories)
                .FirstOrDefaultAsync(h => h.HospitalId == hospitalId);
            if (hospital == null)
            {
                throw new KeyNotFoundException($"Hospital with ID {hospitalId} was not found.");
            }

            hospital.ApprovalStatus = ApprovalStatus.Rejected;
            hospital.IsVerified = false; // Automatic synchronization
            hospital.RejectionReason = dto.Reason;
            hospital.RejectionReportName = dto.ReportDocumentName;
            hospital.RejectionReportUrl = dto.ReportDocumentUrl;
            hospital.UpdatedAt = DateTime.UtcNow;

            await _context.HospitalApprovalHistories.AddAsync(new HospitalApprovalHistory
            {
                HospitalId = hospital.HospitalId,
                Status = ApprovalStatus.Rejected,
                Timestamp = DateTime.UtcNow,
                AdminId = adminId,
                Comments = dto.Reason,
                ReportDocumentName = dto.ReportDocumentName,
                ReportDocumentUrl = dto.ReportDocumentUrl
            });

            await _context.SaveChangesAsync();

            await _notificationService.NotifyHospitalRejectedAsync(hospital, dto.Reason);

            return MapToHospitalDto(hospital);
        }

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
                .Include(h => h.ApprovalHistories)
                .OrderByDescending(h => h.CreatedAt)
                .ToListAsync();

            return hospitals.Select(MapToHospitalDto).ToList();
        }

        private static AdminHospitalResponseDto MapToHospitalDto(Hospital hospital)
        {
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
                ApprovedAt = hospital.ApprovedAt,
                ApprovedByAdminId = hospital.ApprovedByAdminId,
                RejectionReason = hospital.RejectionReason,
                RejectionReportUrl = hospital.RejectionReportUrl,
                RejectionReportName = hospital.RejectionReportName,
                RegistrationNumber = hospital.RegistrationNumber,
                City = hospital.City,
                ContactPersonName = hospital.ContactPersonName,
                ContactPersonPhone = hospital.ContactPersonPhone,
                ContactPersonEmail = hospital.ContactPersonEmail,
                LicenseDocumentUrl = hospital.LicenseDocumentUrl,
                LicenseDocumentName = hospital.LicenseDocumentName,
                AccreditationDocumentUrl = hospital.AccreditationDocumentUrl,
                AccreditationDocumentName = hospital.AccreditationDocumentName,
                ResubmittedAt = hospital.ResubmittedAt,
                UpdatedFields = hospital.UpdatedFields,
                IsSuspended = hospital.IsSuspended,
                SuspendedUntil = hospital.SuspendedUntil,
                SuspensionReason = hospital.SuspensionReason,
                CreatedAt = hospital.CreatedAt,
                UpdatedAt = hospital.UpdatedAt,
                ApprovalHistory = hospital.ApprovalHistories?
                    .OrderBy(h => h.Timestamp)
                    .Select(h => new LifeLink.DTOs.Hospitals.HospitalApprovalHistoryDto
                    {
                        Id = h.Id,
                        Status = h.Status.ToString(),
                        Timestamp = h.Timestamp,
                        AdminId = h.AdminId,
                        AdminName = h.AdminName ?? h.Admin?.FirstName,
                        Comments = h.Comments,
                        ReportDocumentName = h.ReportDocumentName,
                        ReportDocumentUrl = h.ReportDocumentUrl,
                        ChangedFields = h.ChangedFields
                    }).ToList() ?? new List<LifeLink.DTOs.Hospitals.HospitalApprovalHistoryDto>()
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
