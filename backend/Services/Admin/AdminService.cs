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
                r.Status == BloodRequestStatus.Pending || r.Status == BloodRequestStatus.Approved);

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
                .Where(h => h.ApprovalStatus == ApprovalStatus.Pending || !h.IsVerified)
                .OrderByDescending(h => h.CreatedAt)
                .ToListAsync();

            return list.Select(MapToHospitalDto).ToList();
        }

        public async Task<AdminHospitalResponseDto> ApproveHospitalAsync(Guid hospitalId, Guid adminId)
        {
            var hospital = await _context.Hospitals.FindAsync(hospitalId);
            if (hospital == null)
            {
                throw new KeyNotFoundException($"Hospital with ID {hospitalId} was not found.");
            }

            hospital.ApprovalStatus = ApprovalStatus.Approved;
            hospital.IsVerified = true; // Automatic synchronization
            hospital.ApprovedAt = DateTime.UtcNow;
            hospital.ApprovedByAdminId = adminId;
            hospital.RejectionReason = null;
            hospital.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _notificationService.NotifyHospitalApprovedAsync(hospital);

            return MapToHospitalDto(hospital);
        }

        public async Task<AdminHospitalResponseDto> RejectHospitalAsync(Guid hospitalId, Guid adminId, RejectHospitalDto dto)
        {
            var hospital = await _context.Hospitals.FindAsync(hospitalId);
            if (hospital == null)
            {
                throw new KeyNotFoundException($"Hospital with ID {hospitalId} was not found.");
            }

            hospital.ApprovalStatus = ApprovalStatus.Rejected;
            hospital.IsVerified = false; // Automatic synchronization
            hospital.RejectionReason = dto.Reason;
            hospital.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _notificationService.NotifyHospitalRejectedAsync(hospital, dto.Reason);

            return MapToHospitalDto(hospital);
        }

        public async Task<AdminUserResponseDto> SuspendUserAsync(Guid userId, SuspendUserDto dto)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                throw new KeyNotFoundException($"User with ID {userId} was not found.");
            }

            user.IsSuspended = true;
            user.SuspendedUntil = dto.SuspendedUntil;
            user.SuspensionReason = dto.Reason;
            user.AccountStatus = AccountStatus.Suspended;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _notificationService.NotifyUserSuspendedAsync(user, dto.Reason, dto.SuspendedUntil);

            return MapToUserDto(user);
        }

        public async Task<AdminUserResponseDto> ReinstateUserAsync(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                throw new KeyNotFoundException($"User with ID {userId} was not found.");
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
                IsSuspended = hospital.IsSuspended,
                SuspendedUntil = hospital.SuspendedUntil,
                SuspensionReason = hospital.SuspensionReason,
                CreatedAt = hospital.CreatedAt,
                UpdatedAt = hospital.UpdatedAt
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
                CreatedAt = user.CreatedAt
            };
        }
    }
}
