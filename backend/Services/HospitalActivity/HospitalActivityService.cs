using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.HospitalActivity;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.HospitalActivity
{
    public class HospitalActivityService : IHospitalActivityService
    {
        private readonly AppDbContext _context;

        public HospitalActivityService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<ActivityReportResponseDto> SubmitActivityReportAsync(SubmitActivityReportDto dto)
        {
            var hospital = await _context.Hospitals.FindAsync(dto.HospitalId);
            if (hospital == null)
            {
                throw new KeyNotFoundException($"Hospital with ID {dto.HospitalId} was not found.");
            }

            var complaint = await _context.Complaints.FindAsync(dto.ComplaintId);
            if (complaint == null)
            {
                throw new KeyNotFoundException($"Complaint with ID {dto.ComplaintId} was not found.");
            }

            if (complaint.HospitalId != dto.HospitalId)
            {
                throw new InvalidOperationException($"Hospital with ID {dto.HospitalId} is not associated with this complaint investigation.");
            }

            if (complaint.Status == ComplaintStatus.RESOLVED || complaint.Status == ComplaintStatus.REJECTED)
            {
                throw new InvalidOperationException("Cannot submit activity reports for a complaint that has already been resolved or closed.");
            }

            var report = new HospitalActivityReport
            {
                ReportId = Guid.NewGuid(),
                HospitalId = dto.HospitalId,
                ComplaintId = dto.ComplaintId,
                RequestedByAdminId = complaint.AssignedAdminId, // Preserves investigation context
                Title = dto.Title.Trim(),
                Description = dto.Description.Trim(),
                SubmittedAt = DateTime.UtcNow
            };

            // Audit record on complaint
            var auditLog = new ComplaintAuditLog
            {
                AuditId = Guid.NewGuid(),
                ComplaintId = complaint.ComplaintId,
                AdminId = complaint.AssignedAdminId,
                PreviousStatus = complaint.Status.ToString(),
                NewStatus = complaint.Status.ToString(),
                Notes = $"Hospital '{hospital.Name}' submitted activity report: '{dto.Title}'.",
                CreatedAt = DateTime.UtcNow
            };

            await _context.HospitalActivityReports.AddAsync(report);
            await _context.ComplaintAuditLogs.AddAsync(auditLog);
            await _context.SaveChangesAsync();

            return new ActivityReportResponseDto
            {
                ReportId = report.ReportId,
                HospitalId = report.HospitalId,
                HospitalName = hospital.Name,
                ComplaintId = report.ComplaintId,
                ComplaintSubject = complaint.Subject,
                RequestedByAdminId = report.RequestedByAdminId,
                Title = report.Title,
                Description = report.Description,
                SubmittedAt = report.SubmittedAt
            };
        }

        public async Task<List<ActivityReportResponseDto>> GetActivityReportsAsync(Guid? complaintId = null, Guid? hospitalId = null)
        {
            var query = _context.HospitalActivityReports
                .Include(r => r.Hospital)
                .Include(r => r.Complaint)
                .AsQueryable();

            if (complaintId.HasValue)
            {
                query = query.Where(r => r.ComplaintId == complaintId.Value);
            }

            if (hospitalId.HasValue)
            {
                query = query.Where(r => r.HospitalId == hospitalId.Value);
            }

            var list = await query.OrderByDescending(r => r.SubmittedAt).ToListAsync();

            return list.Select(r => new ActivityReportResponseDto
            {
                ReportId = r.ReportId,
                HospitalId = r.HospitalId,
                HospitalName = r.Hospital?.Name ?? string.Empty,
                ComplaintId = r.ComplaintId,
                ComplaintSubject = r.Complaint?.Subject ?? string.Empty,
                RequestedByAdminId = r.RequestedByAdminId,
                Title = r.Title,
                Description = r.Description,
                SubmittedAt = r.SubmittedAt
            }).ToList();
        }

        public async Task<ActivityReportResponseDto?> GetActivityReportByIdAsync(Guid reportId)
        {
            var r = await _context.HospitalActivityReports
                .Include(x => x.Hospital)
                .Include(x => x.Complaint)
                .FirstOrDefaultAsync(x => x.ReportId == reportId);

            if (r == null) return null;

            return new ActivityReportResponseDto
            {
                ReportId = r.ReportId,
                HospitalId = r.HospitalId,
                HospitalName = r.Hospital?.Name ?? string.Empty,
                ComplaintId = r.ComplaintId,
                ComplaintSubject = r.Complaint?.Subject ?? string.Empty,
                RequestedByAdminId = r.RequestedByAdminId,
                Title = r.Title,
                Description = r.Description,
                SubmittedAt = r.SubmittedAt
            };
        }
    }
}
