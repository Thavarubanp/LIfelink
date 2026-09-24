using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Inventory
{
    /// <summary>
    /// Creates a blood group category with its thresholds. Stock itself only arrives as packets from recorded
    /// donations or completed transfers.
    /// </summary>
    public class CreateInventoryDto
    {
        public Guid HospitalId { get; set; } // taken from the signed-in hospital

        [Required]
        public string BloodGroup { get; set; } = string.Empty;

        [Range(0, int.MaxValue, ErrorMessage = "MinimumThreshold cannot be negative.")]
        public int MinimumThreshold { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "MaximumCapacity must be greater than zero.")]
        public int MaximumCapacity { get; set; } = 100;
    }
}
