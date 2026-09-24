using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.Inventory
{
    public class BloodPacketResponseDto
    {
        public Guid PacketId { get; set; }
        public string PacketCode => $"PKT-{PacketId.ToString()[..8].ToUpperInvariant()}";
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
        public List<InventoryTransactionResponseDto>? History { get; set; } // only when a single packet is requested
    }
}
