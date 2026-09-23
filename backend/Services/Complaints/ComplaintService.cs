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
    public class ComplaintService : IComplaintService
    {
        private readonly AppDbContext _context;
        private readonly IAdminNotificationService _notificationService;

        public ComplaintService(AppDbContext context, IAdminNotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        public async Task<ComplaintResponseDto> CreateComplaintAsync(Guid? userId, Guid? hospitalId, CreateComplaintDto dto)
        {
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
                ComplaintType = dto.ComplaintType.Trim(),
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
            var query = _context.Complaints
                .Include(c => c.User)
                .Include(c => c.TargetUser)
                .Include(c => c.Hospital)
                .Include(c => c.AssignedAdmin)
                .Include(c => c.ActivityReports)
                .Include(c => c.AuditLogs)
                    .ThenInclude(a => a.Admin)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ComplaintStatus>(status, true, out var parsedStatus))
            {
                query = query.Where(c => c.Status == parsedStatus);
            }

            var list = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<List<ComplaintResponseDto>> GetMyComplaintsAsync(Guid userId)
        {
            var query = _context.Complaints
                .Include(c => c.User)
                .Include(c => c.TargetUser)
                .Include(c => c.Hospital)
                .Include(c => c.AssignedAdmin)
                .Include(c => c.ActivityReports)
                .Include(c => c.AuditLogs)
                    .ThenInclude(a => a.Admin)
                .Where(c => c.UserId == userId);

            var list = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<ComplaintResponseDto?> GetComplaintByIdAsync(Guid complaintId)
        {
            var complaint = await _context.Complaints
                .Include(c => c.User)
                .Include(c => c.TargetUser)
                .Include(c => c.Hospital)
                .Include(c => c.AssignedAdmin)
                .Include(c => c.ActivityReports)
                .Include(c => c.AuditLogs)
                    .ThenInclude(a => a.Admin)
                .FirstOrDefaultAsync(c => c.ComplaintId == complaintId);

            return complaint == null ? null : MapToDto(complaint);
        }

        public async Task<ComplaintResponseDto> ReviewComplaintAsync(Guid complaintId, Guid adminId, ReviewComplaintDto? dto = null)
        {
            var complaint = await _context.Complaints.FindAsync(complaintId);
            if (complaint == null)
            {
                throw new KeyNotFoundException($"Complaint with ID {complaintId} was not found.");
            }

            if (complaint.Status == ComplaintStatus.CANCELLED || complaint.Status == ComplaintStatus.RESOLVED || complaint.Status == ComplaintStatus.REJECTED)
            {
                throw new InvalidOperationException("Cannot review a closed or cancelled complaint.");
            }

            var previousStatus = complaint.Status.ToString();
            complaint.Status = ComplaintStatus.UNDER_REVIEW;
            complaint.AssignedAdminId = adminId;

            var auditLog = new ComplaintAuditLog
            {
                AuditId = Guid.NewGuid(),
                ComplaintId = complaint.ComplaintId,
                AdminId = adminId,
                PreviousStatus = previousStatus,
                NewStatus = ComplaintStatus.UNDER_REVIEW.ToString(),
                Notes = dto?.Notes ?? "Complaint moved under review by administration.",
                CreatedAt = DateTime.UtcNow
            };

            await _context.ComplaintAuditLogs.AddAsync(auditLog);
            await _context.SaveChangesAsync();

            return await GetComplaintByIdAsync(complaintId) ?? MapToDto(complaint);
        }

        public async Task<ComplaintResponseDto> RequestActivityReportAsync(Guid complaintId, Guid adminId, RequestActivityReportDto dto)
        {
            var complaint = await _context.Complaints.FindAsync(complaintId);
            if (complaint == null)
            {
                throw new KeyNotFoundException($"Complaint with ID {complaintId} was not found.");
            }

            if (complaint.Status == ComplaintStatus.CANCELLED || complaint.Status == ComplaintStatus.RESOLVED || complaint.Status == ComplaintStatus.REJECTED)
            {
                throw new InvalidOperationException("Cannot request evidence for a closed or cancelled complaint.");
            }

            if (!complaint.HospitalId.HasValue)
            {
                throw new InvalidOperationException("Cannot request hospital activity report for a complaint that is not associated with a hospital.");
            }

            var previousStatus = complaint.Status.ToString();
            complaint.Status = ComplaintStatus.AWAITING_INFORMATION;
            complaint.AssignedAdminId = adminId;

            var auditLog = new ComplaintAuditLog
            {
                AuditId = Guid.NewGuid(),
                ComplaintId = complaint.ComplaintId,
                AdminId = adminId,
                PreviousStatus = previousStatus,
                NewStatus = ComplaintStatus.AWAITING_INFORMATION.ToString(),
                Notes = $"Requested activity report from hospital. Instructions: {dto.Instructions}",
                CreatedAt = DateTime.UtcNow
            };

            await _context.ComplaintAuditLogs.AddAsync(auditLog);
            await _context.SaveChangesAsync();

            // Notify target hospital
            await _notificationService.NotifyActivityReportRequestedAsync(complaint, dto.Instructions, complaint.HospitalId.Value);

            return await GetComplaintByIdAsync(complaintId) ?? MapToDto(complaint);
        }

        public async Task<ComplaintResponseDto> ResolveComplaintAsync(Guid complaintId, Guid adminId, ResolveComplaintDto dto)
        {
            var complaint = await _context.Complaints.FindAsync(complaintId);
            if (complaint == null)
            {
                throw new KeyNotFoundException($"Complaint with ID {complaintId} was not found.");
            }

            if (complaint.Status == ComplaintStatus.CANCELLED)
            {
                throw new InvalidOperationException("Cannot resolve a complaint that has been cancelled.");
            }

            if (dto.Status.Equals("RESOLVED", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Admins cannot mark complaints as solved. Only the complaint creator can mark a complaint as solved.");
            }

            if (!Enum.TryParse<ComplaintStatus>(dto.Status, true, out var finalStatus) || finalStatus != ComplaintStatus.REJECTED)
            {
                throw new InvalidOperationException("Admin resolution status must be 'REJECTED'.");
            }

            if (string.IsNullOrWhiteSpace(dto.ResolutionNotes))
            {
                throw new InvalidOperationException("A rejection reason is mandatory when rejecting a complaint.");
            }

            var previousStatus = complaint.Status.ToString();
            complaint.Status = finalStatus;
            complaint.AssignedAdminId = adminId;
            complaint.ResolvedAt = DateTime.UtcNow;
            complaint.ResolutionNotes = dto.ResolutionNotes.Trim();

            var auditLog = new ComplaintAuditLog
            {
                AuditId = Guid.NewGuid(),
                ComplaintId = complaint.ComplaintId,
                AdminId = adminId,
                PreviousStatus = previousStatus,
                NewStatus = finalStatus.ToString(),
                Notes = dto.ResolutionNotes.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            await _context.ComplaintAuditLogs.AddAsync(auditLog);
            await _context.SaveChangesAsync();

            await _notificationService.NotifyComplaintResolvedAsync(complaint);

            return await GetComplaintByIdAsync(complaintId) ?? MapToDto(complaint);
        }

        public async Task<ComplaintResponseDto> SolveComplaintAsync(Guid complaintId, Guid userId, string? notes = null)
        {
            var complaint = await _context.Complaints
                .Include(c => c.AuditLogs)
                .Include(c => c.ActivityReports)
                .FirstOrDefaultAsync(c => c.ComplaintId == complaintId);

            if (complaint == null)
            {
                throw new KeyNotFoundException($"Complaint with ID {complaintId} was not found.");
            }

            if (complaint.UserId != userId)
            {
                throw new UnauthorizedAccessException("Only the complaint creator can mark this complaint as solved.");
            }

            if (complaint.Status == ComplaintStatus.CANCELLED || complaint.Status == ComplaintStatus.RESOLVED || complaint.Status == ComplaintStatus.REJECTED)
            {
                throw new InvalidOperationException("This complaint is already closed or in a terminal state.");
            }

            var previousStatus = complaint.Status.ToString();
            complaint.Status = ComplaintStatus.RESOLVED;
            complaint.ResolvedAt = DateTime.UtcNow;
            complaint.ResolutionNotes = !string.IsNullOrWhiteSpace(notes) ? notes.Trim() : "Marked as solved by complaint creator.";

            var auditLog = new ComplaintAuditLog
            {
                AuditId = Guid.NewGuid(),
                ComplaintId = complaint.ComplaintId,
                AdminId = null,
                PreviousStatus = previousStatus,
                NewStatus = ComplaintStatus.RESOLVED.ToString(),
                Notes = !string.IsNullOrWhiteSpace(notes) ? notes.Trim() : "Marked as solved by complaint creator.",
                CreatedAt = DateTime.UtcNow
            };

            await _context.ComplaintAuditLogs.AddAsync(auditLog);
            await _context.SaveChangesAsync();

            return await GetComplaintByIdAsync(complaintId) ?? MapToDto(complaint);
        }

        public async Task<ComplaintResponseDto> CancelComplaintAsync(Guid complaintId, Guid userId)
        {
            var complaint = await _context.Complaints
                .Include(c => c.AuditLogs)
                .Include(c => c.ActivityReports)
                .FirstOrDefaultAsync(c => c.ComplaintId == complaintId);

            if (complaint == null)
            {
                throw new KeyNotFoundException($"Complaint with ID {complaintId} was not found.");
            }

            if (complaint.UserId != userId)
            {
                throw new UnauthorizedAccessException("Only the complaint creator can cancel this complaint.");
            }

            if (complaint.Status == ComplaintStatus.CANCELLED || complaint.Status == ComplaintStatus.RESOLVED || complaint.Status == ComplaintStatus.REJECTED)
            {
                throw new InvalidOperationException("This complaint is already closed or cancelled.");
            }

            // Permanently remove associated records and the complaint itself from the database
            if (complaint.AuditLogs != null && complaint.AuditLogs.Any())
            {
                _context.ComplaintAuditLogs.RemoveRange(complaint.AuditLogs);
            }

            if (complaint.ActivityReports != null && complaint.ActivityReports.Any())
            {
                _context.HospitalActivityReports.RemoveRange(complaint.ActivityReports);
            }

            _context.Complaints.Remove(complaint);
            await _context.SaveChangesAsync();

            complaint.Status = ComplaintStatus.CANCELLED;
            return MapToDto(complaint);
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
            if (roles.Contains("Doctor"))
            {
                throw new InvalidOperationException("Complaints cannot be filed against doctors. File the complaint against the doctor's hospital instead.");
            }

            if (roles.Contains("Admin") || roles.Contains("HospitalStaff"))
            {
                throw new InvalidOperationException("Complaints can only be filed against individual users or hospitals.");
            }
        }

        private static ComplaintResponseDto MapToDto(Complaint complaint)
        {
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
                    CreatedAt = a.CreatedAt
                }).ToList() ?? new List<ComplaintAuditLogDto>()
            };
        }
    }
}
