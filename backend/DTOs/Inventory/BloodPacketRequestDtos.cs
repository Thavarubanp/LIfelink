using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.Inventory
{
    /// <summary>
    /// Hospital staff enter collected blood as packets. The hospital comes from the signed-in account; each packet gets
    /// its own system-generated tracking number. CollectionDate is a date only ("2026-09-28").
    /// </summary>
    public class CreateBloodPacketsDto
    {
        public const int MaxQuantity = 20;

        public string BloodGroup { get; set; } = string.Empty;
        public DateOnly? CollectionDate { get; set; }
        public int Quantity { get; set; } = 1; // identical packets to create, 1-20
    }

    /// <summary>Editable packet fields. Tracking number, created-by hospital and created date can never change.</summary>
    public class UpdateBloodPacketDto
    {
        public string BloodGroup { get; set; } = string.Empty;
        public DateOnly? CollectionDate { get; set; }
    }

    /// <summary>The specific packets a hospital chose (issue, transfer or donation).</summary>
    public class PacketSelectionDto
    {
        public List<Guid> PacketIds { get; set; } = new();
    }
}
