using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Admin
{
    public class SuspendHospitalDto
    {
        [Required(ErrorMessage = "Suspension reason is required.")]
        [MinLength(3, ErrorMessage = "Suspension reason must be at least 3 characters.")]
        [MaxLength(500, ErrorMessage = "Suspension reason cannot exceed 500 characters.")]
        public string Reason { get; set; } = string.Empty;

        public DateTime? SuspendedUntil { get; set; }
    }
}
