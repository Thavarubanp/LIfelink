using System;

namespace LifeLink.Entities
{
    public class BloodRequestVerification
    {
        public Guid VerificationId { get; set; } = Guid.NewGuid();
        public Guid BloodRequestId { get; set; }
        public Guid DoctorId { get; set; }

        public VerificationStatus Status { get; set; } = VerificationStatus.Pending;
        public string? Notes { get; set; }
        public DateTime? VerifiedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Doctor Doctor { get; set; } = null!;
    }
}
