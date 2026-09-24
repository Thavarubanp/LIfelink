using System;

namespace LifeLink.Entities
{
    public class InventoryTransaction
    {
        public Guid TransactionId { get; set; }
        public Guid InventoryId { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public int Units { get; set; }
        public string Notes { get; set; } = string.Empty;
        public Guid? PacketId { get; set; }          // packet-level audit trail
        public Guid? ReferenceId { get; set; }       // acceptance, transfer or emergency request
        public Guid? PerformedByUserId { get; set; } // null for system actions (expiry, migration)
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Property
        public BloodInventory Inventory { get; set; } = null!;
    }
}
