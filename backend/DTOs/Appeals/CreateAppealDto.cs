using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Appeals
{
    public class CreateAppealDto
    {
        [Required(ErrorMessage = "Reason is required.")]
        [MinLength(10, ErrorMessage = "Reason must be at least 10 characters.")]
        [MaxLength(2000, ErrorMessage = "Reason cannot exceed 2000 characters.")]
        public string Reason { get; set; } = string.Empty;

        public Guid? UserId { get; set; }
        public Guid? HospitalId { get; set; }

        /// <summary>Optional attachment as a data URL (max ~2 MB file), like complaint replies.</summary>
        [MaxLength(2_800_000, ErrorMessage = "Attachment cannot exceed 2 MB.")]
        public string? AttachmentUrl { get; set; }

        [MaxLength(255, ErrorMessage = "Attachment name cannot exceed 255 characters.")]
        public string? AttachmentName { get; set; }
    }
}
