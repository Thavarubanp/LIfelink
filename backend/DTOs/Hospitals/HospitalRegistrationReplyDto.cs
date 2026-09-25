using System.ComponentModel.DataAnnotations;
using LifeLink.Common;

namespace LifeLink.DTOs.Hospitals
{
    /// <summary>
    /// A hospital's reply in its rejected registration's conversation: a message, optionally with corrected details,
    /// replacement documents and one attachment. Blank fields keep their current value.
    /// </summary>
    public class HospitalRegistrationReplyDto
    {
        [Required(ErrorMessage = "A reply message is required.")]
        [StringLength(1000, MinimumLength = 3, ErrorMessage = "The reply message must be 3 to 1000 characters.")]
        public string Message { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Name { get; set; }

        [StringLength(100)]
        public string? LicenseNumber { get; set; }

        [StringLength(100)]
        public string? RegistrationNumber { get; set; }

        [StringLength(500)]
        public string? Address { get; set; }

        [StringLength(100)]
        public string? City { get; set; }

        [RegularExpression(@"^\d{10}$", ErrorMessage = "Hospital contact number must be exactly 10 digits.")]
        public string? ContactNumber { get; set; }

        [StringLength(200, ErrorMessage = "Authorized person name cannot exceed 200 characters.")]
        public string? ContactPersonName { get; set; }

        [RegularExpression(@"^\d{10}$", ErrorMessage = "Authorized person phone number must be exactly 10 digits.")]
        public string? ContactPersonPhone { get; set; }

        [StringLength(200)]
        public string? ContactPersonEmail { get; set; }

        [MaxLength(AttachmentRules.MaxDataUrlLength, ErrorMessage = "Each document cannot exceed 2 MB.")]
        public string? LicenseDocumentUrl { get; set; }

        [StringLength(255)]
        public string? LicenseDocumentName { get; set; }

        [MaxLength(AttachmentRules.MaxDataUrlLength, ErrorMessage = "Each document cannot exceed 2 MB.")]
        public string? AccreditationDocumentUrl { get; set; }

        [StringLength(255)]
        public string? AccreditationDocumentName { get; set; }

        [MaxLength(AttachmentRules.MaxDataUrlLength, ErrorMessage = "Attachment cannot exceed 2 MB.")]
        public string? AttachmentUrl { get; set; }

        [StringLength(255)]
        public string? AttachmentName { get; set; }
    }
}
