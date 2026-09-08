using System;

namespace LifeLink.DTOs.Inventory
{
    public class InventoryResponseDto
    {
        public Guid InventoryId { get; set; }
        public Guid HospitalId { get; set; }
        public string HospitalName { get; set; } = string.Empty;
        public string BloodGroup { get; set; } = string.Empty;
        public int UnitsAvailable { get; set; }
        public int MinimumThreshold { get; set; }
        public int MaximumCapacity { get; set; }
        public bool IsLowStock => UnitsAvailable <= MinimumThreshold;
        public bool IsSurplus => UnitsAvailable >= (int)(MaximumCapacity * 0.8);
        public DateTime LastUpdated { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
