using System;

namespace LifeLink.DTOs.Acceptances
{
    public class AcceptanceResponseDto
    {
        public Guid AcceptanceId { get; set; }
        public Guid BloodRequestId { get; set; }
        public Guid DonorUserId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime AcceptedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? RejectionReason { get; set; }
    }
}
