using System;
using System.Collections.Generic;

namespace LifeLink.Entities
{
    public class BloodInventory
    {
        public Guid InventoryId { get; set; }
        public Guid HospitalId { get; set; }
        public string BloodGroup { get; set; } = string.Empty;
        public int UnitsAvailable { get; set; }
        public int MinimumThreshold { get; set; }
        public int MaximumCapacity { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public int ConcurrencyToken { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public Hospital Hospital { get; set; } = null!;
        public ICollection<InventoryTransaction> Transactions { get; set; } = new List<InventoryTransaction>();
    }
}
