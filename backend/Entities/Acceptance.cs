using System;

namespace LifeLink.Entities
{
    public class Acceptance : IConcurrencyVersioned
    {
        public Guid AcceptanceId { get; set; } = Guid.NewGuid();
        public int ConcurrencyToken { get; set; } // optimistic concurrency (see AppDbContext)
        public Guid BloodRequestId { get; set; }
        public Guid DonorUserId { get; set; } // for a hospital donation: the staff account that accepted
        // Set when a hospital donates packets from its inventory instead of a donor (no screening, doctor approval only)
        public Guid? DonorHospitalId { get; set; }
        public AcceptanceStatus Status { get; set; } = AcceptanceStatus.Accepted;
        public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CancelledAt { get; set; }
        public string? RejectionReason { get; set; }
    }
}
