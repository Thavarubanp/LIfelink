using System;

namespace LifeLink.Entities
{
    public class HospitalTransferRequest : IConcurrencyVersioned
    {
        public Guid TransferRequestId { get; set; }
        public int ConcurrencyToken { get; set; } // optimistic concurrency (see AppDbContext)
        public Guid SenderHospitalId { get; set; }
        public Guid ReceiverHospitalId { get; set; }
        public string BloodGroup { get; set; } = string.Empty;
        public int UnitsRequested { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        // "Request": receiver asks the sender for blood. "Offer": sender offers blood to the receiver.
        public string TransferType { get; set; } = TransferTypes.Request;
        public string? RejectionReason { get; set; }
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ApprovedAt { get; set; }
        public DateTime? RejectedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public Hospital SenderHospital { get; set; } = null!;
        public Hospital ReceiverHospital { get; set; } = null!;
    }

    public static class TransferTypes
    {
        public const string Request = "Request";
        public const string Offer = "Offer";
    }
}
