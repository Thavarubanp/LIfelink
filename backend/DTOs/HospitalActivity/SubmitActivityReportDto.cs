using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.HospitalActivity
{
    public class SubmitActivityReportDto
    {
        [Required(ErrorMessage = "HospitalId is required.")]
        public Guid HospitalId { get; set; }

        [Required(ErrorMessage = "ComplaintId is required.")]
        public Guid ComplaintId { get; set; }

        [Required(ErrorMessage = "Title is required.")]
        [MinLength(5, ErrorMessage = "Title must be at least 5 characters.")]
        [MaxLength(200, ErrorMessage = "Title cannot exceed 200 characters.")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Description is required.")]
        [MinLength(10, ErrorMessage = "Description must be at least 10 characters.")]
        [MaxLength(4000, ErrorMessage = "Description cannot exceed 4000 characters.")]
        public string Description { get; set; } = string.Empty;
    }
}
