using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Appeals;
using LifeLink.DTOs.Complaints;
using LifeLink.Entities;
using LifeLink.Services.Admin;
using LifeLink.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Appeals
{
    /// <summary>
    /// Appeals are conversation threads between the suspended appellant (a donor/patient, or the staff of a suspended
    /// hospital) and the Admin. Messages alternate appellant -> admin -> appellant. Reject keeps the thread open;
    /// Approve reinstates and ends it; Close makes it read-only. Doctors never take part in governance.
    /// </summary>
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
            Guid? userId;
            Guid? hospitalId;

            if (currentUserId.HasValue)
            {
                var user = await _context.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.UserId == currentUserId.Value)
                    ?? throw new KeyNotFoundException($"User with ID {currentUserId.Value} was not found.");
                var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();

                if (roles.Contains("Doctor"))
                {
                    throw new InvalidOperationException("Doctors cannot submit governance appeals.");
                }
                if (AccountLifecycleHelper.IsRemoved(user) || user.IsPermanentlyBlocked)
                {
                    throw new InvalidOperationException("Your account has been permanently blocked. You cannot submit further appeals.");
                }

                var hospital = roles.Contains("HospitalStaff") ? await GovernanceAccessHelper.GetGovernedHospitalAsync(_context, user, roles) : null;
                userId = user.UserId;
                hospitalId = hospital?.IsSuspended == true ? hospital.HospitalId : null;
                if (hospitalId == null && !user.IsSuspended)
                {
                    throw new InvalidOperationException("Account is not currently suspended. Appeals can only be submitted for suspended accounts.");
                }
            }
            else
            {
                // Internal path: the appellant is given explicitly
                userId = dto.UserId;
                hospitalId = dto.HospitalId;
                if (!userId.HasValue && !hospitalId.HasValue)
                {
                    throw new ArgumentException("Appeal must specify either a UserId or HospitalId.");
                }
                if (userId.HasValue)
                {
                    var user = await _context.Users.FindAsync(userId.Value) ?? throw new KeyNotFoundException($"User with ID {userId.Value} was not found.");
                    if (AccountLifecycleHelper.IsRemoved(user) || user.IsPermanentlyBlocked)
                        throw new InvalidOperationException("Your account has been permanently blocked. You cannot submit further appeals.");
                    if (!user.IsSuspended)
                        throw new InvalidOperationException("Account is not currently suspended. Appeals can only be submitted for suspended accounts.");
                }
                else
                {
                    var hospital = await _context.Hospitals.FindAsync(hospitalId!.Value) ?? throw new KeyNotFoundException($"Hospital with ID {hospitalId.Value} was not found.");
                    if (!hospital.IsSuspended)
                        throw new InvalidOperationException("Hospital is not currently suspended. Appeals can only be submitted for suspended hospitals.");
                }
            }

            // One open thread at a time
            var hasOpenThread = await _context.Appeals.AnyAsync(a =>
                (hospitalId != null ? a.HospitalId == hospitalId : a.UserId == userId && a.HospitalId == null) &&
                a.Status != AppealStatus.APPROVED && a.Status != AppealStatus.CLOSED);
            if (hasOpenThread)
            {
                throw new InvalidOperationException("You already have an open appeal. Continue the conversation in that thread.");
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
            await _context.AppealMessages.AddAsync(NewMessage(appeal.AppealId, null, dto.Reason, dto.AttachmentUrl, dto.AttachmentName));
            await _context.SaveChangesAsync();

            await _notificationService.NotifyAdminAsync("New Appeal Submitted", "A suspended account submitted an appeal.");
            return await GetAppealByIdAsync(appeal.AppealId) ?? MapToDto(appeal);
        }

        public async Task<List<AppealResponseDto>> GetAppealsAsync(string? status = null)
        {
            var query = WithDetails();
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AppealStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(a => a.Status == parsedStatus);
            }
            var list = await query.OrderByDescending(a => a.SubmittedAt).ToListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<AppealResponseDto?> GetAppealByIdAsync(Guid appealId)
        {
            var appeal = await WithDetails().FirstOrDefaultAsync(a => a.AppealId == appealId);
            return appeal == null ? null : MapToDto(appeal);
        }

        /// <summary>The caller's own appeals, or their hospital's appeals (staff, and doctors read-only).</summary>
        public async Task<List<AppealResponseDto>> GetMyAppealsAsync(Guid userId)
        {
            var user = await _context.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return new List<AppealResponseDto>();
            var hospital = await GovernanceAccessHelper.GetGovernedHospitalAsync(_context, user, user.UserRoles.Select(ur => ur.Role.Name).ToList());
            var hospitalId = hospital?.HospitalId;

            var list = await WithDetails()
                .Where(a => (hospitalId != null && a.HospitalId == hospitalId) || (hospitalId == null && a.UserId == userId))
                .OrderByDescending(a => a.SubmittedAt)
                .ToListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<AppealResponseDto> AppellantReplyAsync(Guid appealId, Guid userId, ReviewComplaintDto dto)
        {
            var appeal = await LoadAsync(appealId);
            var user = await _context.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.UserId == userId)
                ?? throw new KeyNotFoundException("User not found.");
            var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
            if (roles.Contains("Doctor"))
            {
                throw new UnauthorizedAccessException("Doctors can view the appeal thread but cannot reply.");
            }
            var hospital = roles.Contains("HospitalStaff") ? await GovernanceAccessHelper.GetGovernedHospitalAsync(_context, user, roles) : null;
            var isParticipant = appeal.HospitalId != null ? hospital?.HospitalId == appeal.HospitalId : appeal.UserId == userId;
            if (!isParticipant)
            {
                throw new UnauthorizedAccessException("Only the appellant can reply to this appeal.");
            }
            EnsureOpen(appeal);
            if (!IsAppellantTurn(appeal))
            {
                throw new InvalidOperationException("You can reply after an administrator responds.");
            }

            await _context.AppealMessages.AddAsync(NewMessage(appeal.AppealId, null, dto.Notes, dto.AttachmentUrl, dto.AttachmentName));
            appeal.Status = AppealStatus.PENDING; // back in the admin's queue (also after a rejection)
            await _context.SaveChangesAsync();

            await _notificationService.NotifyAdminAsync("Appeal Reply Received", "An appellant replied to their appeal.");
            return await GetAppealByIdAsync(appealId) ?? MapToDto(appeal);
        }

        public async Task<AppealResponseDto> AdminReplyAsync(Guid appealId, Guid adminId, ReviewComplaintDto dto)
        {
            var appeal = await LoadAsync(appealId);
            EnsureNotSelf(appeal, adminId);
            EnsureOpen(appeal);
            if (IsAppellantTurn(appeal))
            {
                throw new InvalidOperationException("Waiting for the appellant to respond before the next admin reply.");
            }

            await _context.AppealMessages.AddAsync(NewMessage(appeal.AppealId, adminId, dto.Notes, dto.AttachmentUrl, dto.AttachmentName));
            appeal.ReviewedByAdminId = adminId;
            await _context.SaveChangesAsync();

            await NotifyAppellantAsync(appeal, "Admin Replied to Your Appeal", "An administrator replied to your appeal.");
            return await GetAppealByIdAsync(appealId) ?? MapToDto(appeal);
        }

        public async Task<AppealResponseDto> ApproveAppealAsync(Guid appealId, Guid adminId, ReviewAppealDto dto)
        {
            var appeal = await LoadAsync(appealId);
            EnsureNotSelf(appeal, adminId);
            EnsureOpen(appeal);

            RecordDecision(appeal, AppealStatus.APPROVED, adminId, dto.AdminResponse);

            // AUTOMATIC REINSTATEMENT (a hospital appeal reinstates the hospital; a user appeal the user)
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
            else if (appeal.UserId.HasValue)
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

            await _context.SaveChangesAsync();
            await _notificationService.NotifyAppealApprovedAsync(appeal);
            return await GetAppealByIdAsync(appealId) ?? MapToDto(appeal);
        }

        /// <summary>Rejects the appeal; the suspension stays but the thread remains open for further replies.</summary>
        public async Task<AppealResponseDto> RejectAppealAsync(Guid appealId, Guid adminId, ReviewAppealDto dto)
        {
            var appeal = await LoadAsync(appealId);
            EnsureNotSelf(appeal, adminId);
            EnsureOpen(appeal);

            RecordDecision(appeal, AppealStatus.REJECTED, adminId, dto.AdminResponse);
            await _context.SaveChangesAsync();

            await _notificationService.NotifyAppealRejectedAsync(appeal);
            return await GetAppealByIdAsync(appealId) ?? MapToDto(appeal);
        }

        /// <summary>Permanently closes the thread (read-only). The suspension itself is unchanged.</summary>
        public async Task<AppealResponseDto> CloseAppealAsync(Guid appealId, Guid adminId, ReviewAppealDto dto)
        {
            var appeal = await LoadAsync(appealId);
            EnsureNotSelf(appeal, adminId);
            EnsureOpen(appeal);

            RecordDecision(appeal, AppealStatus.CLOSED, adminId, dto.AdminResponse);
            await _context.SaveChangesAsync();

            await NotifyAppellantAsync(appeal, "Appeal Closed", "An administrator closed your appeal thread.");
            return await GetAppealByIdAsync(appealId) ?? MapToDto(appeal);
        }

        /// <summary>Permanently blocks the donor/patient behind a user appeal. Hospitals can never be permanently blocked.</summary>
        public async Task<AppealResponseDto> PermanentlyBlockAsync(Guid appealId, Guid adminId, ReviewAppealDto dto)
        {
            var appeal = await LoadAsync(appealId);
            EnsureNotSelf(appeal, adminId);
            if (appeal.HospitalId.HasValue || !appeal.UserId.HasValue)
            {
                throw new InvalidOperationException("Hospitals cannot be permanently blocked; they can only be suspended and reinstated.");
            }

            var user = await _context.Users.FindAsync(appeal.UserId.Value) ?? throw new KeyNotFoundException("User not found.");
            await AccountLifecycleHelper.EnsureDonorPatientAccountAsync(_context, user.UserId, "permanently blocked");

            RecordDecision(appeal, AppealStatus.CLOSED, adminId, dto.AdminResponse);
            await AccountLifecycleHelper.PermanentlyBlockAsync(_context, user);
            await _context.SaveChangesAsync();

            return await GetAppealByIdAsync(appealId) ?? MapToDto(appeal);
        }

        private IQueryable<Appeal> WithDetails() => _context.Appeals
            .Include(a => a.User)
            .Include(a => a.Hospital)
            .Include(a => a.ReviewedByAdmin)
            .Include(a => a.Messages).ThenInclude(m => m.Admin);

        private async Task<Appeal> LoadAsync(Guid appealId) =>
            await _context.Appeals.Include(a => a.Messages).FirstOrDefaultAsync(a => a.AppealId == appealId)
            ?? throw new KeyNotFoundException($"Appeal with ID {appealId} was not found.");

        private static bool IsClosed(Appeal appeal) => appeal.Status is AppealStatus.APPROVED or AppealStatus.CLOSED;

        private static void EnsureOpen(Appeal appeal)
        {
            if (IsClosed(appeal))
            {
                throw new InvalidOperationException("This appeal thread is closed and read-only.");
            }
        }

        private static void EnsureNotSelf(Appeal appeal, Guid adminId)
        {
            if (appeal.UserId == adminId)
            {
                throw new InvalidOperationException("You cannot perform governance actions on your own appeal.");
            }
        }

        // Appellant's turn when the admin spoke last (a decision message counts as an admin message)
        private static bool IsAppellantTurn(Appeal appeal) =>
            appeal.Messages.OrderBy(m => m.CreatedAt).LastOrDefault()?.AdminId != null;

        private void RecordDecision(Appeal appeal, AppealStatus status, Guid adminId, string response)
        {
            appeal.Status = status;
            appeal.ReviewedByAdminId = adminId;
            appeal.ReviewedAt = DateTime.UtcNow;
            appeal.AdminResponse = response.Trim();
            _context.AppealMessages.Add(NewMessage(appeal.AppealId, adminId, $"[{status}] {response}", null, null));
        }

        private static AppealMessage NewMessage(Guid appealId, Guid? adminId, string? text, string? attachmentUrl, string? attachmentName)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("A message is required.");
            }
            AttachmentRules.EnsureValidIfPresent(attachmentUrl); // data URL with content, or no file
            var hasAttachment = AttachmentRules.HasContent(attachmentUrl);
            return new AppealMessage
            {
                MessageId = Guid.NewGuid(),
                AppealId = appealId,
                AdminId = adminId,
                Message = text.Trim(),
                AttachmentUrl = hasAttachment ? attachmentUrl : null,
                AttachmentName = hasAttachment ? (string.IsNullOrWhiteSpace(attachmentName) ? "attachment" : attachmentName.Trim()) : null,
                CreatedAt = DateTime.UtcNow
            };
        }

        private Task NotifyAppellantAsync(Appeal appeal, string title, string message) =>
            _notificationService.NotifyUserAsync(appeal.UserId, appeal.UserId.HasValue ? null : appeal.HospitalId, title, message);

        private static AppealResponseDto MapToDto(Appeal appeal)
        {
            var closed = IsClosed(appeal);
            var appellantTurn = IsAppellantTurn(appeal);
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
                AdminResponse = appeal.AdminResponse,
                IsClosed = closed,
                AwaitingAdminReply = !closed && !appellantTurn,
                CanAppellantReply = !closed && appellantTurn,
                Messages = appeal.Messages.OrderBy(m => m.CreatedAt).Select(m => new AppealMessageDto
                {
                    MessageId = m.MessageId,
                    FromAdmin = m.AdminId != null,
                    AdminEmail = m.Admin?.Email,
                    Message = m.Message,
                    AttachmentUrl = AttachmentRules.HasContent(m.AttachmentUrl) ? m.AttachmentUrl : null,
                    AttachmentName = AttachmentRules.HasContent(m.AttachmentUrl) ? m.AttachmentName : null,
                    CreatedAt = m.CreatedAt
                }).ToList()
            };
        }
    }
}
