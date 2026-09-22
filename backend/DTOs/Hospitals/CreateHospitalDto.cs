using System.ComponentModel.DataAnnotations;

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

        [StringLength(50)]
        public string ContactNumber { get; set; } = string.Empty;

        [EmailAddress]
        [StringLength(200)]
        public string Email { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Password { get; set; }

        public string? RegistrationNumber { get; set; }
        public string? City { get; set; }
        public string? ContactPersonName { get; set; }
        public string? ContactPersonPhone { get; set; }
        public string? ContactPersonEmail { get; set; }
        public string? LicenseDocumentUrl { get; set; }
        public string? LicenseDocumentName { get; set; }
        public string? AccreditationDocumentUrl { get; set; }
        public string? AccreditationDocumentName { get; set; }
    }
}
