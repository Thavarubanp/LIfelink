using System.ComponentModel.DataAnnotations;
using LifeLink.Common;

namespace LifeLink.DTOs.Admin
{
    public class RejectHospitalDto
    {
        [Required(ErrorMessage = "Rejection reason is required.")]
        [MinLength(3, ErrorMessage = "Rejection reason must be at least 3 characters.")]
        [MaxLength(500, ErrorMessage = "Rejection reason cannot exceed 500 characters.")]
        public string Reason { get; set; } = string.Empty;

        [StringLength(255)]
        public string? ReportDocumentName { get; set; }

        [MaxLength(AttachmentRules.MaxDataUrlLength, ErrorMessage = "The review report cannot exceed 2 MB.")]
        public string? ReportDocumentUrl { get; set; }
    }
}
