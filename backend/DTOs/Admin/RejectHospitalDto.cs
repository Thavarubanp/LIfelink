using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Admin
{
    public class RejectHospitalDto
    {
        [Required(ErrorMessage = "Rejection reason is required.")]
        [MinLength(3, ErrorMessage = "Rejection reason must be at least 3 characters.")]
        [MaxLength(500, ErrorMessage = "Rejection reason cannot exceed 500 characters.")]
        public string Reason { get; set; } = string.Empty;
        public string? ReportDocumentName { get; set; }
        public string? ReportDocumentUrl { get; set; }
    }
}
