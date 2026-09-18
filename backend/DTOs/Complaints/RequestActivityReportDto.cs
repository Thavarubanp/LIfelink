using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Complaints
{
    public class RequestActivityReportDto
    {
        [Required(ErrorMessage = "Instructions are required.")]
        [MinLength(5, ErrorMessage = "Instructions must be at least 5 characters.")]
        [MaxLength(1000, ErrorMessage = "Instructions cannot exceed 1000 characters.")]
        public string Instructions { get; set; } = string.Empty;

        [MaxLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters.")]
        public string? Notes { get; set; }
    }
}
