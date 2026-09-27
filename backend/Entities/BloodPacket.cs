using System;

namespace LifeLink.Entities
{
    /// <summary>
    /// One unit of whole blood (440 ml) tracked individually. Packets are created by a recorded donation, by hospital
    /// staff entering collected stock, or by the one-time legacy migration / Development seeder. TrackingNumber,
    /// CreatedByHospitalId and CreatedAt never change (enforced in AppDbContext). A transfer keeps every field and only
    /// changes HospitalId. Used packets are kept with status Issued or Donated, never deleted. Every change is written to
    /// InventoryTransactions with the PacketId.
    /// </summary>
    public class BloodPacket
    {
        public const int StandardVolumeMl = 440;

        public Guid PacketId { get; set; } = Guid.NewGuid();
        public string TrackingNumber { get; set; } = string.Empty; // PKT-00001234, from a database sequence
        public Guid CreatedByHospitalId { get; set; }              // hospital that created the packet (only it may edit)
        public Guid HospitalId { get; set; }                       // current owner
        public string BloodGroup { get; set; } = string.Empty;
        public int VolumeMl { get; set; } = StandardVolumeMl;
        public DateTime CollectionDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string Status { get; set; } = BloodPacketStatus.Available;
        public string Source { get; set; } = BloodPacketSource.Donation;
        public Guid? SourceReferenceId { get; set; } // AcceptanceId for donations
        public Guid? HeldForReferenceId { get; set; } // acceptance or transfer holding a Reserved packet
        public int ConcurrencyToken { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Hospital Hospital { get; set; } = null!;
        public Hospital CreatedByHospital { get; set; } = null!;
    }

    public static class BloodPacketStatus
    {
        public const string Available = "Available";
        public const string Reserved = "Reserved"; // held by a pending transfer offer or hospital donation
        public const string Issued = "Issued";
        public const string Donated = "Donated";   // given to a patient's blood request by a hospital
        public const string Expired = "Expired";
    }

    public static class BloodPacketSource
    {
        public const string Donation = "Donation";
        public const string Manual = "Manual"; // entered by hospital staff (collected stock)
        public const string Legacy = "Legacy"; // stock that existed before packet tracking (migration only)
        public const string Seed = "Seed";     // Development seeder only
    }
}
