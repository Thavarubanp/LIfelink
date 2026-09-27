using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.Inventory
{
    public class BloodPacketResponseDto
    {
        public Guid PacketId { get; set; }
        public string TrackingNumber { get; set; } = string.Empty;
        public string PacketCode => TrackingNumber; // kept for existing screens
        public Guid CreatedByHospitalId { get; set; }
        public string CreatedByHospitalName { get; set; } = string.Empty;
        public Guid HospitalId { get; set; }
        public string HospitalName { get; set; } = string.Empty;
        public string BloodGroup { get; set; } = string.Empty;
        public int VolumeMl { get; set; }
        public DateTime CollectionDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public Guid? SourceReferenceId { get; set; }
        public bool IsExpiringSoon { get; set; }
        public DateTime CreatedAt { get; set; }
        // True only for the created-by hospital while it still owns the packet and it is Available
        public bool CanEdit { get; set; }
        public List<InventoryTransactionResponseDto>? History { get; set; } // only when a single packet is requested
    }
}
