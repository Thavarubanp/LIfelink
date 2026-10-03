using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.Acceptances
{
    /// <summary>A hospital accepts a public blood request by donating these Available packets of the required group.</summary>
    public class CreateHospitalDonationDto
    {
        public Guid BloodRequestId { get; set; }
        public List<Guid> PacketIds { get; set; } = new();
    }

    /// <summary>The assigned doctor's decision on a hospital donation: Notes for approval, Reason (required) for rejection.</summary>
    public class HospitalDonationDecisionDto
    {
        public string? Notes { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>A packet offered or donated by a hospital to a blood request.</summary>
    public class DonatedPacketDto
    {
        public Guid PacketId { get; set; }
        public string TrackingNumber { get; set; } = string.Empty;
        public string BloodGroup { get; set; } = string.Empty;
        public DateTime CollectionDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
