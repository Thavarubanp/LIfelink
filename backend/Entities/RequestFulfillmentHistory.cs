using System;

namespace LifeLink.Entities
{
    public class RequestFulfillmentHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid BloodRequestId { get; set; }
        public Guid AcceptanceId { get; set; }
        public Guid DonorUserId { get; set; }
        public DateTime FulfilledAt { get; set; } = DateTime.UtcNow;
    }
}
