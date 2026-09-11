using System;

namespace LifeLink.Entities
{
    public class BloodRequest
    {
        public Guid BloodRequestId { get; set; } = Guid.NewGuid();
        public Guid PatientUserId { get; set; }
        public Guid HospitalId { get; set; }
        public string BloodGroup { get; set; } = string.Empty;
        public int UnitsRequired { get; set; }
        public int FulfilledUnits { get; set; } = 0;
        public int ConcurrencyToken { get; set; } = 0;
        public string Reason { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public BloodRequestStatus Status { get; set; } = BloodRequestStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiryDate { get; set; }
        public DateTime? CancelledAt { get; set; }
    }
}
