using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Complaints
{
    public class CreateComplaintDto
    {
        [Required(ErrorMessage = "ComplaintType is required.")]
        [MaxLength(100, ErrorMessage = "ComplaintType cannot exceed 100 characters.")]
        public string ComplaintType { get; set; } = string.Empty;

        [Required(ErrorMessage = "Subject is required.")]
        [MinLength(5, ErrorMessage = "Subject must be at least 5 characters.")]
        [MaxLength(200, ErrorMessage = "Subject cannot exceed 200 characters.")]
        public string Subject { get; set; } = string.Empty;

        [Required(ErrorMessage = "Description is required.")]
        [MinLength(10, ErrorMessage = "Description must be at least 10 characters.")]
        [MaxLength(2000, ErrorMessage = "Description cannot exceed 2000 characters.")]
        public string Description { get; set; } = string.Empty;

        public Guid? HospitalId { get; set; }
    }
}
