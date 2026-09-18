using System;

namespace LifeLink.Entities
{
    public class HospitalActivityReport
    {
        public Guid ReportId { get; set; } = Guid.NewGuid();
        public Guid HospitalId { get; set; }
        public Guid ComplaintId { get; set; }
        public Guid? RequestedByAdminId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Hospital Hospital { get; set; } = null!;
        public Complaint Complaint { get; set; } = null!;
        public User? RequestedByAdmin { get; set; }
    }
}
