using System;
using System.Collections.Generic;

namespace LifeLink.Entities
{
    public class Complaint
    {
        public Guid ComplaintId { get; set; } = Guid.NewGuid();
        public Guid? UserId { get; set; }
        public Guid? HospitalId { get; set; }
        public string ComplaintType { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ComplaintStatus Status { get; set; } = ComplaintStatus.OPEN;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? AssignedAdminId { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolutionNotes { get; set; }

        // Navigation properties
        public User? User { get; set; }
        public Hospital? Hospital { get; set; }
        public User? AssignedAdmin { get; set; }
        public ICollection<HospitalActivityReport> ActivityReports { get; set; } = new List<HospitalActivityReport>();
        public ICollection<ComplaintAuditLog> AuditLogs { get; set; } = new List<ComplaintAuditLog>();
    }
}
