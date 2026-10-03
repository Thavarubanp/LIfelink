using System;
using System.Collections.Generic;

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

        // Hospital donation: DonorName/Email/Phone are the hospital's; Packets are the offered packets
        public Guid? DonorHospitalId { get; set; }
        public List<DonatedPacketDto> Packets { get; set; } = new();
    }
}
