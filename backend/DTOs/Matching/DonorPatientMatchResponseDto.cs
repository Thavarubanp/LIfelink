using System;

namespace LifeLink.DTOs.Matching
{
    public class DonorPatientMatchResponseDto
    {
        public Guid MatchId { get; set; }
        public Guid BloodRequestId { get; set; }
        public Guid DonorUserId { get; set; }
        public string? DonorName { get; set; }
        public Guid? DoctorId { get; set; } // null when the doctor account was deleted
        public string DoctorName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsRemovedFromPublicDashboard { get; set; }
        public string? Notes { get; set; }
        public DateTime MatchedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
