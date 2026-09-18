using System;

namespace LifeLink.DTOs.HospitalActivity
{
    public class ActivityReportResponseDto
    {
        public Guid ReportId { get; set; }
        public Guid HospitalId { get; set; }
        public string HospitalName { get; set; } = string.Empty;
        public Guid ComplaintId { get; set; }
        public string ComplaintSubject { get; set; } = string.Empty;
        public Guid? RequestedByAdminId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
    }
}
