using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Complaints
{
    /// <summary>
    /// Complaint reply (admin or creator) or "mark as solved" notes. Attachments apply to replies only.
    /// </summary>
    public class ReviewComplaintDto
    {
        [MaxLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters.")]
        public string? Notes { get; set; }

        /// <summary>Optional attachment as a data URL (max ~2 MB file).</summary>
        [MaxLength(2_800_000, ErrorMessage = "Attachment cannot exceed 2 MB.")]
        public string? AttachmentUrl { get; set; }

        [MaxLength(255, ErrorMessage = "Attachment name cannot exceed 255 characters.")]
        public string? AttachmentName { get; set; }
    }
}
