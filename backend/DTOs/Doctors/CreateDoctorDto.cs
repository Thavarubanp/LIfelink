using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Doctors
{
    public class CreateDoctorDto
    {
        [Required]
        public Guid HospitalId { get; set; }

        public Guid? UserId { get; set; }

        [Required]
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(200)]
        public string Email { get; set; } = string.Empty;

        [StringLength(50)]
        public string PhoneNumber { get; set; } = string.Empty;

        [StringLength(100)]
        public string LicenseNumber { get; set; } = string.Empty;

        [StringLength(100)]
        public string Specialization { get; set; } = string.Empty;
    }
}
