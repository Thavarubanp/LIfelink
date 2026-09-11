using System;

namespace LifeLink.DTOs.Acceptances
{
    public class RequestAcceptanceDetailDto
    {
        public Guid AcceptanceId { get; set; }
        public Guid BloodRequestId { get; set; }
        public Guid DonorUserId { get; set; }
        public string DonorName { get; set; } = string.Empty;
        public string DonorEmail { get; set; } = string.Empty;
        public string DonorPhoneNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime AcceptedAt { get; set; }
        public string? RejectionReason { get; set; }
    }
}
