using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Appeals
{
    public class ReviewAppealDto
    {
        [Required(ErrorMessage = "AdminResponse is required.")]
        [MinLength(5, ErrorMessage = "AdminResponse must be at least 5 characters.")]
        [MaxLength(2000, ErrorMessage = "AdminResponse cannot exceed 2000 characters.")]
        public string AdminResponse { get; set; } = string.Empty;
    }
}
