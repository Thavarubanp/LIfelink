using System;

namespace LifeLink.DTOs.Hospitals
{
    /// <summary>One entry of a hospital's registration conversation, oldest first.</summary>
    public class HospitalApprovalHistoryDto
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty; // Submitted, Rejected, AdminComment, HospitalReply, Approved
        public bool FromAdmin { get; set; }
        public DateTime Timestamp { get; set; }
        public Guid? AdminId { get; set; }     // admin views only
        public string? AdminName { get; set; } // admin views only (the acting admin's email)
        public string? Message { get; set; }
        public string? ChangedFields { get; set; } // one "Field: old -> new" line per corrected detail
        public string? AttachmentName { get; set; }
        public string? AttachmentUrl { get; set; }
    }
}
