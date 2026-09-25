using System;

namespace LifeLink.Entities
{
    /// <summary>
    /// One entry of a hospital's registration conversation. Admin entries carry AdminId; hospital entries do not.
    /// ReportDocumentName/Url hold the entry's attachment and ChangedFields the details a hospital reply corrected.
    /// </summary>
    public class HospitalApprovalHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid HospitalId { get; set; }
        public RegistrationEntryType Status { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Guid? AdminId { get; set; }
        public string? AdminName { get; set; }
        public string? Comments { get; set; }
        public string? ReportDocumentName { get; set; }
        public string? ReportDocumentUrl { get; set; }
        public string? ChangedFields { get; set; }

        // Navigation Properties
        public Hospital Hospital { get; set; } = null!;
        public User? Admin { get; set; }
    }
}
