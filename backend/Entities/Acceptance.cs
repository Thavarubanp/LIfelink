using System;

namespace LifeLink.Entities
{
    public class Acceptance
    {
        public Guid AcceptanceId { get; set; } = Guid.NewGuid();
        public Guid BloodRequestId { get; set; }
        public Guid DonorUserId { get; set; }
        public AcceptanceStatus Status { get; set; } = AcceptanceStatus.Accepted;
        public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CancelledAt { get; set; }
        public string? RejectionReason { get; set; }
    }
}
