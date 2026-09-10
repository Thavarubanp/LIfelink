using System;

namespace LifeLink.Entities
{
    public class DonorPatientMatch
    {
        public Guid MatchId { get; set; } = Guid.NewGuid();
        public Guid BloodRequestId { get; set; }
        public Guid DonorUserId { get; set; }
        public Guid DoctorId { get; set; }

        public MatchStatus Status { get; set; } = MatchStatus.Matched;
        public bool IsRemovedFromPublicDashboard { get; set; } = true;
        public string? Notes { get; set; }
        public DateTime MatchedAt { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Doctor Doctor { get; set; } = null!;
        public User? DonorUser { get; set; }
    }
}
