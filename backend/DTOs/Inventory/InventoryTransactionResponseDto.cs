using System;

namespace LifeLink.DTOs.Inventory
{
    public class InventoryTransactionResponseDto
    {
        public Guid TransactionId { get; set; }
        public Guid InventoryId { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public int Units { get; set; }
        public string Notes { get; set; } = string.Empty;
        public Guid? PacketId { get; set; }
        public Guid? ReferenceId { get; set; }
        public Guid? PerformedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
