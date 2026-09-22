using System;

namespace LifeLink.DTOs.Hospitals
{
    public class HospitalApprovalHistoryDto
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public Guid? AdminId { get; set; }
        public string? AdminName { get; set; }
        public string? Comments { get; set; }
        public string? ReportDocumentName { get; set; }
        public string? ReportDocumentUrl { get; set; }
        public string? ChangedFields { get; set; }
    }
}
