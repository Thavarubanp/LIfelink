using System;

namespace LifeLink.DTOs.Verification
{
    public class BloodRequestVerificationResponseDto
    {
        public Guid VerificationId { get; set; }
        public Guid BloodRequestId { get; set; }
        public Guid? DoctorId { get; set; } // null when the doctor account was deleted
        public string DoctorName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
