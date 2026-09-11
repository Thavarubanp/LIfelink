using System;

namespace LifeLink.DTOs.BloodRequests
{
    public class RequestFulfillmentHistoryResponseDto
    {
        public Guid Id { get; set; }
        public Guid BloodRequestId { get; set; }
        public Guid AcceptanceId { get; set; }
        public Guid DonorUserId { get; set; }
        public DateTime FulfilledAt { get; set; }
    }
}
