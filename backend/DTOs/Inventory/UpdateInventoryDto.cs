using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Inventory
{
    /// <summary>
    /// Updates thresholds and optionally issues the packets listed in IssuePacketIds (AuditNotes is the reason).
    /// UnitsAvailable is the count of Available packets and cannot be changed directly.
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

        // Packets of this blood group to issue (chosen by staff)
        public List<Guid>? IssuePacketIds { get; set; }
    }
}
