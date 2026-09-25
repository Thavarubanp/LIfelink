using System;
using System.ComponentModel.DataAnnotations;
using LifeLink.Common;

namespace LifeLink.DTOs.Admin
{
    /// <summary>Admin message in a rejected registration's conversation.</summary>
    public class HospitalRegistrationCommentDto
    {
        [Required(ErrorMessage = "A comment is required.")]
        [StringLength(1000, MinimumLength = 3, ErrorMessage = "The comment must be 3 to 1000 characters.")]
        public string Message { get; set; } = string.Empty;

        [MaxLength(AttachmentRules.MaxDataUrlLength, ErrorMessage = "Attachment cannot exceed 2 MB.")]
        public string? AttachmentUrl { get; set; }

        [StringLength(255)]
        public string? AttachmentName { get; set; }

        /// <summary>The latest conversation entry the admin had seen; a newer hospital reply makes the request fail with 409.</summary>
        public Guid? LastSeenEntryId { get; set; }
    }
}
