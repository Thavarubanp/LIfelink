using System;

namespace LifeLink.Entities
{
    public class HospitalTransferRequest
    {
        public Guid TransferRequestId { get; set; }
        public Guid SenderHospitalId { get; set; }
        public Guid ReceiverHospitalId { get; set; }
        public string BloodGroup { get; set; } = string.Empty;
        public int UnitsRequested { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ApprovedAt { get; set; }
        public DateTime? RejectedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public Hospital SenderHospital { get; set; } = null!;
        public Hospital ReceiverHospital { get; set; } = null!;
    }
}
