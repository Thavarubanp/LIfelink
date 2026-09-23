using System;

namespace LifeLink.DTOs.Verification
{
    public class DonorVerificationResponseDto
    {
        public Guid DonorVerificationId { get; set; }
        public Guid AcceptanceId { get; set; }
        public Guid? DoctorId { get; set; } // null when the doctor account was deleted
        public string DoctorName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? MedicalReportSummary { get; set; }
        public string? Notes { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
