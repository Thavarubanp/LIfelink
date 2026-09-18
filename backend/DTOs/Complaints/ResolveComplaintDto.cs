using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Complaints
{
    public class ResolveComplaintDto
    {
        [Required(ErrorMessage = "Status is required.")]
        [RegularExpression("^(RESOLVED|REJECTED)$", ErrorMessage = "Status must be either 'RESOLVED' or 'REJECTED'.")]
        public string Status { get; set; } = "RESOLVED";

        [Required(ErrorMessage = "ResolutionNotes are required.")]
        [MinLength(5, ErrorMessage = "ResolutionNotes must be at least 5 characters.")]
        [MaxLength(2000, ErrorMessage = "ResolutionNotes cannot exceed 2000 characters.")]
        public string ResolutionNotes { get; set; } = string.Empty;
    }
}
