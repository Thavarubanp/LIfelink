using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Complaints
{
    public class ReviewComplaintDto
    {
        [MaxLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters.")]
        public string? Notes { get; set; }
    }
}
