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
    }
}
