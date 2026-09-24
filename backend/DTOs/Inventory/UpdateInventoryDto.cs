using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Inventory
{
    /// <summary>
    /// Updates thresholds. A lower UnitsAvailable issues that many packets (earliest expiry first) with AuditNotes
    /// as the reason; stock can never be raised here.
    /// </summary>
    public class UpdateInventoryDto
    {
        [Range(0, int.MaxValue, ErrorMessage = "UnitsAvailable cannot be negative.")]
        public int? UnitsAvailable { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "MinimumThreshold cannot be negative.")]
        public int MinimumThreshold { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "MaximumCapacity must be greater than zero.")]
        public int MaximumCapacity { get; set; }

        public string? AuditNotes { get; set; }
    }
}
