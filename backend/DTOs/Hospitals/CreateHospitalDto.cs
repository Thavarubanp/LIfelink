using System.ComponentModel.DataAnnotations;
using LifeLink.Common;

namespace LifeLink.DTOs.Hospitals
{
    public class CreateHospitalDto
    {
        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [StringLength(100)]
        public string LicenseNumber { get; set; } = string.Empty;

        [StringLength(500)]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = "Hospital contact number is required.")]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "Hospital contact number must be exactly 10 digits.")]
        public string ContactNumber { get; set; } = string.Empty;

        [EmailAddress]
        [StringLength(200)]
        public string Email { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Password { get; set; }

        public string? RegistrationNumber { get; set; }
        public string? City { get; set; }

        [Required(ErrorMessage = "Authorized person name is required.")]
        [StringLength(200, ErrorMessage = "Authorized person name cannot exceed 200 characters.")]
        public string? ContactPersonName { get; set; }

        [Required(ErrorMessage = "Authorized person phone number is required.")]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "Authorized person phone number must be exactly 10 digits.")]
        public string? ContactPersonPhone { get; set; }

        public string? ContactPersonEmail { get; set; }

        [MaxLength(AttachmentRules.MaxDataUrlLength, ErrorMessage = "Each document cannot exceed 2 MB.")]
        public string? LicenseDocumentUrl { get; set; }

        [StringLength(255)]
        public string? LicenseDocumentName { get; set; }

        [MaxLength(AttachmentRules.MaxDataUrlLength, ErrorMessage = "Each document cannot exceed 2 MB.")]
        public string? AccreditationDocumentUrl { get; set; }

        [StringLength(255)]
        public string? AccreditationDocumentName { get; set; }
    }
}
