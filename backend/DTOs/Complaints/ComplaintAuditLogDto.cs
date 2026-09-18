using System;

namespace LifeLink.DTOs.Complaints
{
    public class ComplaintAuditLogDto
    {
        public Guid AuditId { get; set; }
        public Guid ComplaintId { get; set; }
        public Guid? AdminId { get; set; }
        public string? AdminEmail { get; set; }
        public string PreviousStatus { get; set; } = string.Empty;
        public string NewStatus { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
