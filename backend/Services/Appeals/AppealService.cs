using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Appeals;
using LifeLink.Entities;
using LifeLink.Services.Admin;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Appeals
{
    public class AppealService : IAppealService
    {
        private readonly AppDbContext _context;
        private readonly IAdminNotificationService _notificationService;

        public AppealService(AppDbContext context, IAdminNotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        public async Task<AppealResponseDto> SubmitAppealAsync(CreateAppealDto dto, Guid? currentUserId = null)
        {
            var userId = currentUserId ?? dto.UserId;
            var hospitalId = dto.HospitalId;

            // If neither is specified, throw validation error
            if (!userId.HasValue && !hospitalId.HasValue)
            {
                throw new ArgumentException("Appeal must specify either a UserId or HospitalId.");
            }

            // Verify that the entity is suspended and not permanently blocked
            if (userId.HasValue)
            {
                var user = await _context.Users.FindAsync(userId.Value);
                if (user == null)
                {
                    throw new KeyNotFoundException($"User with ID {userId.Value} was not found.");
                }
                if (user.IsPermanentlyBlocked)
                {
                    throw new InvalidOperationException("Your account has been permanently blocked. You cannot submit further appeals.");
                }
                if (!user.IsSuspended && user.AccountStatus != AccountStatus.Suspended)
                {
                    throw new InvalidOperationException("Account is not currently suspended. Appeals can only be submitted for suspended accounts.");
                }
            }
            else if (hospitalId.HasValue)
            {
                var hospital = await _context.Hospitals.FindAsync(hospitalId.Value);
                if (hospital == null)
                {
                    throw new KeyNotFoundException($"Hospital with ID {hospitalId.Value} was not found.");
                }
                if (hospital.IsPermanentlyBlocked)
                {
                    throw new InvalidOperationException("This hospital has been permanently blocked. You cannot submit further appeals.");
                }
                if (!hospital.IsSuspended)
                {
                    throw new InvalidOperationException("Hospital is not currently suspended. Appeals can only be submitted for suspended hospitals.");
                }
            }

            var appeal = new Appeal
            {
                AppealId = Guid.NewGuid(),
                UserId = userId,
                HospitalId = hospitalId,
                Reason = dto.Reason.Trim(),
                Status = AppealStatus.PENDING,
                SubmittedAt = DateTime.UtcNow
            };

            await _context.Appeals.AddAsync(appeal);
            await _context.SaveChangesAsync();

            return await GetAppealByIdAsync(appeal.AppealId) ?? MapToDto(appeal);
        }

        public async Task<List<AppealResponseDto>> GetAppealsAsync(string? status = null)
        {
            var query = _context.Appeals
                .Include(a => a.User)
                .Include(a => a.Hospital)
                .Include(a => a.ReviewedByAdmin)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AppealStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(a => a.Status == parsedStatus);
            }

            var list = await query.OrderByDescending(a => a.SubmittedAt).ToListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<AppealResponseDto?> GetAppealByIdAsync(Guid appealId)
        {
            var appeal = await _context.Appeals
                .Include(a => a.User)
                .Include(a => a.Hospital)
                .Include(a => a.ReviewedByAdmin)
                .FirstOrDefaultAsync(a => a.AppealId == appealId);

            return appeal == null ? null : MapToDto(appeal);
        }

        public async Task<List<AppealResponseDto>> GetMyAppealsAsync(Guid userId)
        {
            // Also include hospital appeals for hospital staff
            var doctor = await _context.Doctors.AsNoTracking()
                .FirstOrDefaultAsync(d => d.UserId == userId);

            var list = await _context.Appeals
                .Include(a => a.User)
                .Include(a => a.Hospital)
                .Include(a => a.ReviewedByAdmin)
                .Where(a => a.UserId == userId ||
                            (doctor != null && a.HospitalId == doctor.HospitalId))
                .OrderByDescending(a => a.SubmittedAt)
                .ToListAsync();

            return list.Select(MapToDto).ToList();
        }

        public async Task<AppealResponseDto> ApproveAppealAsync(Guid appealId, Guid adminId, ReviewAppealDto dto)
        {
            var appeal = await _context.Appeals.FindAsync(appealId);
            if (appeal == null)
            {
                throw new KeyNotFoundException($"Appeal with ID {appealId} was not found.");
            }

            appeal.Status = AppealStatus.APPROVED;
            appeal.ReviewedByAdminId = adminId;
            appeal.ReviewedAt = DateTime.UtcNow;
            appeal.AdminResponse = dto.AdminResponse.Trim();

            // AUTOMATIC REINSTATEMENT
            if (appeal.UserId.HasValue)
            {
                var user = await _context.Users.FindAsync(appeal.UserId.Value);
                if (user != null)
                {
                    user.IsSuspended = false;
                    user.SuspendedUntil = null;
                    user.SuspensionReason = null;
                    user.AccountStatus = AccountStatus.Active;
                    user.UpdatedAt = DateTime.UtcNow;
                }
            }

            if (appeal.HospitalId.HasValue)
            {
                var hospital = await _context.Hospitals.FindAsync(appeal.HospitalId.Value);
                if (hospital != null)
                {
                    hospital.IsSuspended = false;
                    hospital.SuspendedUntil = null;
                    hospital.SuspensionReason = null;
                    hospital.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();

            await _notificationService.NotifyAppealApprovedAsync(appeal);

            return await GetAppealByIdAsync(appealId) ?? MapToDto(appeal);
        }

        public async Task<AppealResponseDto> RejectAppealAsync(Guid appealId, Guid adminId, ReviewAppealDto dto)
        {
            var appeal = await _context.Appeals.FindAsync(appealId);
            if (appeal == null)
            {
                throw new KeyNotFoundException($"Appeal with ID {appealId} was not found.");
            }

            appeal.Status = AppealStatus.REJECTED;
            appeal.ReviewedByAdminId = adminId;
            appeal.ReviewedAt = DateTime.UtcNow;
            appeal.AdminResponse = dto.AdminResponse.Trim();

            await _context.SaveChangesAsync();

            await _notificationService.NotifyAppealRejectedAsync(appeal);

            return await GetAppealByIdAsync(appealId) ?? MapToDto(appeal);
        }

        public async Task<AppealResponseDto> PermanentlyBlockAsync(Guid appealId, Guid adminId, ReviewAppealDto dto)
        {
            var appeal = await _context.Appeals.FindAsync(appealId);
            if (appeal == null)
            {
                throw new KeyNotFoundException($"Appeal with ID {appealId} was not found.");
            }

            // Mark this appeal as rejected
            appeal.Status = AppealStatus.REJECTED;
            appeal.ReviewedByAdminId = adminId;
            appeal.ReviewedAt = DateTime.UtcNow;
            appeal.AdminResponse = dto.AdminResponse.Trim();

            // Set permanent block on the entity
            if (appeal.UserId.HasValue)
            {
                var user = await _context.Users.FindAsync(appeal.UserId.Value);
                if (user != null)
                {
                    user.IsPermanentlyBlocked = true;
                    user.UpdatedAt = DateTime.UtcNow;
                }
            }

            if (appeal.HospitalId.HasValue)
            {
                var hospital = await _context.Hospitals.FindAsync(appeal.HospitalId.Value);
                if (hospital != null)
                {
                    hospital.IsPermanentlyBlocked = true;
                    hospital.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();

            return await GetAppealByIdAsync(appealId) ?? MapToDto(appeal);
        }

        private static AppealResponseDto MapToDto(Appeal appeal)
        {
            return new AppealResponseDto
            {
                AppealId = appeal.AppealId,
                UserId = appeal.UserId,
                UserEmail = appeal.User?.Email,
                HospitalId = appeal.HospitalId,
                HospitalName = appeal.Hospital?.Name,
                Reason = appeal.Reason,
                Status = appeal.Status.ToString(),
                SubmittedAt = appeal.SubmittedAt,
                ReviewedByAdminId = appeal.ReviewedByAdminId,
                ReviewedAt = appeal.ReviewedAt,
                AdminResponse = appeal.AdminResponse
            };
        }
    }
}
