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
        public string? AttachmentUrl { get; set; }
        public string? AttachmentName { get; set; }
        public bool IsReply { get; set; } // true for admin/creator messages (status unchanged)
        public DateTime CreatedAt { get; set; }
    }
}
