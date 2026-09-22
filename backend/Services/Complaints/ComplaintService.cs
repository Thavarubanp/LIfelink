using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Complaints;
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
            var complaint = new Complaint
            {
                ComplaintId = Guid.NewGuid(),
                UserId = userId,
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

            if (!Enum.TryParse<ComplaintStatus>(dto.Status, true, out var finalStatus) ||
                (finalStatus != ComplaintStatus.RESOLVED && finalStatus != ComplaintStatus.REJECTED))
            {
                throw new InvalidOperationException("Resolution status must be either 'RESOLVED' or 'REJECTED'.");
            }

            var previousStatus = complaint.Status.ToString();
            complaint.Status = finalStatus;
            complaint.AssignedAdminId = adminId;
            complaint.ResolvedAt = DateTime.UtcNow;
            complaint.ResolutionNotes = dto.ResolutionNotes;

            var auditLog = new ComplaintAuditLog
            {
                AuditId = Guid.NewGuid(),
                ComplaintId = complaint.ComplaintId,
                AdminId = adminId,
                PreviousStatus = previousStatus,
                NewStatus = finalStatus.ToString(),
                Notes = dto.ResolutionNotes,
                CreatedAt = DateTime.UtcNow
            };

            await _context.ComplaintAuditLogs.AddAsync(auditLog);
            await _context.SaveChangesAsync();

            await _notificationService.NotifyComplaintResolvedAsync(complaint);

            return await GetComplaintByIdAsync(complaintId) ?? MapToDto(complaint);
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
