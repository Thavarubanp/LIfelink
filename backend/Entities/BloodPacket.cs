using System;

namespace LifeLink.Entities
{
    /// <summary>
    /// One unit of whole blood (440 ml) tracked individually. Packets are created only by a recorded donation
    /// (or the one-time legacy migration / Development seeder) and never by hand. A transfer keeps the PacketId and
    /// only changes HospitalId. Every change is written to InventoryTransactions with the PacketId.
    /// </summary>
    public class BloodPacket
    {
        public const int StandardVolumeMl = 440;

        public Guid PacketId { get; set; } = Guid.NewGuid();
        public Guid HospitalId { get; set; } // current owner
        public string BloodGroup { get; set; } = string.Empty;
        public int VolumeMl { get; set; } = StandardVolumeMl;
        public DateTime CollectionDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string Status { get; set; } = BloodPacketStatus.Available;
        public string Source { get; set; } = BloodPacketSource.Donation;
        public Guid? SourceReferenceId { get; set; } // AcceptanceId for donations
        public int ConcurrencyToken { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Hospital Hospital { get; set; } = null!;
    }

    public static class BloodPacketStatus
    {
        public const string Available = "Available";
        public const string Issued = "Issued";
        public const string Expired = "Expired";
    }

    public static class BloodPacketSource
    {
        public const string Donation = "Donation";
        public const string Legacy = "Legacy"; // stock that existed before packet tracking (migration only)
        public const string Seed = "Seed";     // Development seeder only
    }
}
