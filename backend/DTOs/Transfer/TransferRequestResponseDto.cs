using System;

namespace LifeLink.DTOs.Transfer
{
    public class TransferRequestResponseDto
    {
        public Guid TransferRequestId { get; set; }
        public Guid SenderHospitalId { get; set; }
        public string SenderHospitalName { get; set; } = string.Empty;
        public Guid ReceiverHospitalId { get; set; }
        public string ReceiverHospitalName { get; set; } = string.Empty;
        public string BloodGroup { get; set; } = string.Empty;
        public int UnitsRequested { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string TransferType { get; set; } = string.Empty;
        public Guid CreatedByHospitalId { get; set; }
        public string? RejectionReason { get; set; }
        public List<Guid> PacketIds { get; set; } = new(); // packets moved when the transfer completed
        public DateTime RequestedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime? RejectedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
