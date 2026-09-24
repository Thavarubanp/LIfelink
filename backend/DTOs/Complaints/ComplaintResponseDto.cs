using System;
using System.Collections.Generic;
using LifeLink.DTOs.HospitalActivity;

namespace LifeLink.DTOs.Complaints
{
    public class ComplaintResponseDto
    {
        public Guid ComplaintId { get; set; }
        public Guid? UserId { get; set; }
        public string? UserEmail { get; set; }
        public Guid? HospitalId { get; set; }
        public string? HospitalName { get; set; }
        public Guid? TargetUserId { get; set; }
        public string? TargetUserName { get; set; }
        public string? TargetUserEmail { get; set; }
        public string ComplaintType { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public Guid? AssignedAdminId { get; set; }
        public string? AssignedAdminEmail { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolutionNotes { get; set; }
        public int ActivityReportsCount { get; set; }
        public List<ActivityReportResponseDto> ActivityReports { get; set; } = new List<ActivityReportResponseDto>();
        public List<ComplaintAuditLogDto> AuditLogs { get; set; } = new List<ComplaintAuditLogDto>();

        // Reply turn (replies alternate creator -> admin -> creator ...; the description is the creator's first message)
        public bool AwaitingAdminReply { get; set; }
        public bool CanCreatorReply { get; set; }
    }
}
