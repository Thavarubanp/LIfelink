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
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Property
        public BloodInventory Inventory { get; set; } = null!;
    }
}
