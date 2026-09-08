using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Emergency
{
    public class EmergencyRequestCreateDto
    {
        [Required]
        public Guid HospitalId { get; set; }

        [Required]
        public string BloodGroup { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "UnitsRequired must be greater than zero.")]
        public int UnitsRequired { get; set; }

        [Required]
        public string Priority { get; set; } = string.Empty;

        [Required]
        public string Reason { get; set; } = string.Empty;
    }
}
