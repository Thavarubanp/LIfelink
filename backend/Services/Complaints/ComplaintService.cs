using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Complaints;
using LifeLink.DTOs.HospitalActivity;
using LifeLink.Entities;
using LifeLink.Services.Admin;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Complaints
{
    /// <summary>
    /// Complaint workflow: Users and Hospital Staff create complaints; replies alternate creator -> admin -> creator
    /// (the description is the creator's first message). Admins can only reply. Only the creator can mark a complaint
    /// solved (RESOLVED, read-only) or delete it (any status). Replies are ComplaintAuditLog rows whose status is unchanged.
    /// </summary>
    public class ComplaintService : IComplaintService
    {
        public static readonly string[] UserCategories =
            { "Donation Process", "Blood Request", "Hospital Service", "Account Issue", "Technical Issue", "Other" };

        public static readonly string[] HospitalCategories =
            { "Donor Misconduct", "Fake Blood Request", "Policy Violation", "Account Issue", "Technical Issue", "Other" };

        private readonly AppDbContext _context;
        private readonly IAdminNotificationService _notificationService;

        public ComplaintService(AppDbContext context, IAdminNotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        public async Task<ComplaintResponseDto> CreateComplaintAsync(Guid? userId, Guid? hospitalId, CreateComplaintDto dto)
        {
            if (!userId.HasValue)
            {
                throw new UnauthorizedAccessException("User identity could not be retrieved from token.");
            }

            var isHospitalStaff = await _context.UserRoles
                .AnyAsync(ur => ur.UserId == userId.Value && ur.Role.Name == "HospitalStaff");
            var categories = isHospitalStaff ? HospitalCategories : UserCategories;
            var category = categories.FirstOrDefault(c => c.Equals(dto.ComplaintType.Trim(), StringComparison.OrdinalIgnoreCase));
            if (category == null)
            {
                throw new InvalidOperationException($"Invalid complaint category. Choose one of: {string.Join(", ", categories)}.");
            }

            if (dto.TargetUserId.HasValue)
            {
                await ValidateTargetUserAsync(dto.TargetUserId.Value, userId);
            }

            var complaint = new Complaint
            {
                ComplaintId = Guid.NewGuid(),
                UserId = userId,
                TargetUserId = dto.TargetUserId,
                HospitalId = dto.HospitalId ?? hospitalId,
                ComplaintType = category,
                Subject = dto.Subject.Trim(),
                Description = dto.Description.Trim(),
                Status = ComplaintStatus.OPEN,
                CreatedAt = DateTime.UtcNow
            };

            var auditLog = new ComplaintAuditLog
            {
                AuditId = Guid.NewGuid(),
                ComplaintId = complaint.ComplaintId,
                AdminId = null,
                PreviousStatus = "NONE",
                NewStatus = ComplaintStatus.OPEN.ToString(),
                Notes = "Complaint submitted.",
                CreatedAt = DateTime.UtcNow
            };

            await _context.Complaints.AddAsync(complaint);
            await _context.ComplaintAuditLogs.AddAsync(auditLog);
            await _context.SaveChangesAsync();

            return await GetComplaintByIdAsync(complaint.ComplaintId) ?? MapToDto(complaint);
        }

        public async Task<List<ComplaintResponseDto>> GetComplaintsAsync(string? status = null)
        {
            var query = WithDetails();

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ComplaintStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(c => c.Status == parsedStatus);
            }

            var list = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<List<ComplaintResponseDto>> GetMyComplaintsAsync(Guid userId)
        {
            var list = await WithDetails().Where(c => c.UserId == userId).OrderByDescending(c => c.CreatedAt).ToListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<ComplaintResponseDto?> GetComplaintByIdAsync(Guid complaintId)
        {
            var complaint = await WithDetails().FirstOrDefaultAsync(c => c.ComplaintId == complaintId);
            return complaint == null ? null : MapToDto(complaint);
        }

        public async Task<ComplaintResponseDto> AdminReplyAsync(Guid complaintId, Guid adminId, ReviewComplaintDto dto)
        {
            var complaint = await LoadWithLogsAsync(complaintId);
            EnsureOpen(complaint);
            if (!IsAdminTurn(complaint))
            {
                throw new InvalidOperationException("Waiting for the complaint creator to respond before the next admin reply.");
            }

            complaint.AssignedAdminId = adminId;
            await AddReplyAsync(complaint, adminId, dto);

            await _notificationService.NotifyComplaintCreatorAsync(complaint,
                "Admin Replied to Your Complaint",
                $"An administrator replied to your complaint '{complaint.Subject}'.");

            return await GetComplaintByIdAsync(complaintId) ?? MapToDto(complaint);
        }

        public async Task<ComplaintResponseDto> CreatorReplyAsync(Guid complaintId, Guid userId, ReviewComplaintDto dto)
        {
            var complaint = await LoadWithLogsAsync(complaintId);
            EnsureCreator(complaint, userId, "reply to");
            EnsureOpen(complaint);
            if (IsAdminTurn(complaint))
            {
                throw new InvalidOperationException("You can reply after an administrator responds.");
            }

            await AddReplyAsync(complaint, null, dto);

            await _notificationService.NotifyComplaintAdminsAsync(complaint,
                "Complaint Reply Received",
                $"The creator replied to complaint '{complaint.Subject}'.");

            return await GetComplaintByIdAsync(complaintId) ?? MapToDto(complaint);
        }

        public async Task<ComplaintResponseDto> SolveComplaintAsync(Guid complaintId, Guid userId, string? notes = null)
        {
            var complaint = await LoadWithLogsAsync(complaintId);
            EnsureCreator(complaint, userId, "mark as solved");
            EnsureOpen(complaint);

            var previousStatus = complaint.Status.ToString();
            var resolutionNotes = !string.IsNullOrWhiteSpace(notes) ? notes.Trim() : "Marked as solved by complaint creator.";
            complaint.Status = ComplaintStatus.RESOLVED;
            complaint.ResolvedAt = DateTime.UtcNow;
            complaint.ResolutionNotes = resolutionNotes;

            await _context.ComplaintAuditLogs.AddAsync(new ComplaintAuditLog
            {
                AuditId = Guid.NewGuid(),
                ComplaintId = complaint.ComplaintId,
                AdminId = null,
                PreviousStatus = previousStatus,
                NewStatus = ComplaintStatus.RESOLVED.ToString(),
                Notes = resolutionNotes,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            await _notificationService.NotifyComplaintAdminsAsync(complaint,
                "Complaint Marked as Solved",
                $"The creator marked complaint '{complaint.Subject}' as solved.");

            return await GetComplaintByIdAsync(complaintId) ?? MapToDto(complaint);
        }

        /// <summary>Creator permanently deletes the complaint (any status) with its audit log and activity reports.</summary>
        public async Task DeleteComplaintAsync(Guid complaintId, Guid userId)
        {
            var complaint = await _context.Complaints
                .Include(c => c.AuditLogs)
                .Include(c => c.ActivityReports)
                .FirstOrDefaultAsync(c => c.ComplaintId == complaintId);

            if (complaint == null)
            {
                throw new KeyNotFoundException($"Complaint with ID {complaintId} was not found.");
            }
            EnsureCreator(complaint, userId, "delete");

            _context.ComplaintAuditLogs.RemoveRange(complaint.AuditLogs);
            _context.HospitalActivityReports.RemoveRange(complaint.ActivityReports);
            _context.Complaints.Remove(complaint);
            await _context.SaveChangesAsync();

            await _notificationService.NotifyComplaintAdminsAsync(complaint,
                "Complaint Deleted",
                $"The creator deleted complaint '{complaint.Subject}'.");
        }

        private IQueryable<Complaint> WithDetails() => _context.Complaints
            .Include(c => c.User)
            .Include(c => c.TargetUser)
            .Include(c => c.Hospital)
            .Include(c => c.AssignedAdmin)
            .Include(c => c.ActivityReports)
            .Include(c => c.AuditLogs)
                .ThenInclude(a => a.Admin);

        private async Task<Complaint> LoadWithLogsAsync(Guid complaintId) =>
            await _context.Complaints.Include(c => c.AuditLogs).FirstOrDefaultAsync(c => c.ComplaintId == complaintId)
            ?? throw new KeyNotFoundException($"Complaint with ID {complaintId} was not found.");

        private static void EnsureCreator(Complaint complaint, Guid userId, string action)
        {
            if (complaint.UserId != userId)
            {
                throw new UnauthorizedAccessException($"Only the complaint creator can {action} this complaint.");
            }
        }

        // Resolved (and legacy rejected/cancelled) complaints are read-only
        private static void EnsureOpen(Complaint complaint)
        {
            if (complaint.Status is ComplaintStatus.RESOLVED or ComplaintStatus.REJECTED or ComplaintStatus.CANCELLED)
            {
                throw new InvalidOperationException("This complaint is closed and read-only.");
            }
        }

        private static bool IsReply(ComplaintAuditLog log) => log.PreviousStatus == log.NewStatus;

        // Admin's turn when no reply exists yet (the description counts as the creator's message) or the creator replied last
        private static bool IsAdminTurn(Complaint complaint)
        {
            var lastReply = complaint.AuditLogs.Where(IsReply).OrderBy(a => a.CreatedAt).LastOrDefault();
            return lastReply == null || lastReply.AdminId == null;
        }

        private async Task AddReplyAsync(Complaint complaint, Guid? adminId, ReviewComplaintDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Notes))
            {
                throw new InvalidOperationException("A reply message is required.");
            }

            var hasAttachment = !string.IsNullOrWhiteSpace(dto.AttachmentUrl);
            if (hasAttachment && !dto.AttachmentUrl!.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid attachment.");
            }

            var status = complaint.Status.ToString();
            await _context.ComplaintAuditLogs.AddAsync(new ComplaintAuditLog
            {
                AuditId = Guid.NewGuid(),
                ComplaintId = complaint.ComplaintId,
                AdminId = adminId,
                PreviousStatus = status,
                NewStatus = status,
                Notes = dto.Notes.Trim(),
                AttachmentUrl = hasAttachment ? dto.AttachmentUrl : null,
                AttachmentName = hasAttachment ? (string.IsNullOrWhiteSpace(dto.AttachmentName) ? "attachment" : dto.AttachmentName.Trim()) : null,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Complaints may target Users only; doctors (hospital-managed) and admins cannot be targeted.
        /// </summary>
        private async Task ValidateTargetUserAsync(Guid targetUserId, Guid? complainantId)
        {
            if (complainantId.HasValue && complainantId.Value == targetUserId)
            {
                throw new InvalidOperationException("You cannot file a complaint against yourself.");
            }

            var target = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserId == targetUserId);
            if (target == null)
            {
                throw new InvalidOperationException("The selected user was not found.");
            }

            var roles = target.UserRoles.Select(ur => ur.Role.Name).ToList();
            if (roles.Contains("Doctor") || roles.Contains("Admin") || roles.Contains("HospitalStaff"))
            {
                throw new InvalidOperationException("Complaints can only be filed against individual users or hospitals.");
            }
        }

        private static ComplaintResponseDto MapToDto(Complaint complaint)
        {
            var isOpen = complaint.Status is not (ComplaintStatus.RESOLVED or ComplaintStatus.REJECTED or ComplaintStatus.CANCELLED);
            var adminTurn = IsAdminTurn(complaint);

            return new ComplaintResponseDto
            {
                ComplaintId = complaint.ComplaintId,
                UserId = complaint.UserId,
                UserEmail = complaint.User?.Email,
                HospitalId = complaint.HospitalId,
                HospitalName = complaint.Hospital?.Name,
                TargetUserId = complaint.TargetUserId,
                TargetUserName = complaint.TargetUser != null ? $"{complaint.TargetUser.FirstName} {complaint.TargetUser.LastName}".Trim() : null,
                TargetUserEmail = complaint.TargetUser?.Email,
                ComplaintType = complaint.ComplaintType,
                Subject = complaint.Subject,
                Description = complaint.Description,
                Status = complaint.Status.ToString(),
                CreatedAt = complaint.CreatedAt,
                AssignedAdminId = complaint.AssignedAdminId,
                AssignedAdminEmail = complaint.AssignedAdmin?.Email,
                ResolvedAt = complaint.ResolvedAt,
                ResolutionNotes = complaint.ResolutionNotes,
                AwaitingAdminReply = isOpen && adminTurn,
                CanCreatorReply = isOpen && !adminTurn,
                ActivityReportsCount = complaint.ActivityReports?.Count ?? 0,
                ActivityReports = complaint.ActivityReports?.OrderBy(r => r.SubmittedAt).Select(r => new ActivityReportResponseDto
                {
                    ReportId = r.ReportId,
                    HospitalId = r.HospitalId,
                    HospitalName = r.Hospital?.Name ?? complaint.Hospital?.Name ?? "Hospital Facility",
                    ComplaintId = r.ComplaintId,
                    ComplaintSubject = complaint.Subject,
                    RequestedByAdminId = r.RequestedByAdminId,
                    Title = r.Title,
                    Description = r.Description,
                    SubmittedAt = r.SubmittedAt
                }).ToList() ?? new List<ActivityReportResponseDto>(),
                AuditLogs = complaint.AuditLogs?.OrderBy(a => a.CreatedAt).Select(a => new ComplaintAuditLogDto
                {
                    AuditId = a.AuditId,
                    ComplaintId = a.ComplaintId,
                    AdminId = a.AdminId,
                    AdminEmail = a.Admin?.Email,
                    PreviousStatus = a.PreviousStatus,
                    NewStatus = a.NewStatus,
                    Notes = a.Notes,
                    AttachmentUrl = a.AttachmentUrl,
                    AttachmentName = a.AttachmentName,
                    IsReply = IsReply(a),
                    CreatedAt = a.CreatedAt
                }).ToList() ?? new List<ComplaintAuditLogDto>()
            };
        }
    }
}
