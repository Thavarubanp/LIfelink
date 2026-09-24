using System;

namespace LifeLink.Entities
{
    public class ComplaintAuditLog
    {
        public Guid AuditId { get; set; } = Guid.NewGuid();
        public Guid ComplaintId { get; set; }
        public Guid? AdminId { get; set; }
        public string PreviousStatus { get; set; } = string.Empty;
        public string NewStatus { get; set; } = string.Empty;
        public string? Notes { get; set; }
        // Optional reply attachment, stored as a data URL like hospital documents
        public string? AttachmentUrl { get; set; }
        public string? AttachmentName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Complaint Complaint { get; set; } = null!;
        public User? Admin { get; set; }
    }
}
