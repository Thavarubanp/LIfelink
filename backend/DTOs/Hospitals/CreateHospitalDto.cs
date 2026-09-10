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
    }
}
