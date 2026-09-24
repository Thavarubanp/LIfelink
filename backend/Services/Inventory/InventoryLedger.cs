using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Inventory
{
    /// <summary>
    /// The only place that creates, moves or consumes blood packets. Every packet change writes one
    /// InventoryTransaction row (PacketId + ReferenceId + actor) and adjusts BloodInventory.UnitsAvailable in the
    /// same unit of work. Callers call SaveChanges once, so stock and audit trail commit together. Concurrency
    /// tokens on packets and inventory rows make concurrent changes to the same stock fail instead of drifting.
    /// </summary>
    public static class InventoryLedger
    {
        /// <summary>Returns the tracked (hospital, blood group) inventory row, creating an empty one when missing.</summary>
        public static async Task<BloodInventory> GetOrCreateInventoryAsync(AppDbContext context, Guid hospitalId, string bloodGroup)
        {
            var inventory = context.BloodInventories.Local
                                .FirstOrDefault(i => i.HospitalId == hospitalId && i.BloodGroup == bloodGroup)
                            ?? await context.BloodInventories
                                .FirstOrDefaultAsync(i => i.HospitalId == hospitalId && i.BloodGroup == bloodGroup);
            if (inventory != null) return inventory;

            var now = DateTime.UtcNow;
            inventory = new BloodInventory
            {
                InventoryId = Guid.NewGuid(),
                HospitalId = hospitalId,
                BloodGroup = bloodGroup,
                UnitsAvailable = 0,
                MinimumThreshold = 0,
                MaximumCapacity = 100,
                LastUpdated = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            await context.BloodInventories.AddAsync(inventory);
            return inventory;
        }

        /// <summary>
        /// Creates packets for collected blood. Only a recorded donation (or the Development seeder) may call this;
        /// expiry follows the owning hospital's shelf-life setting.
        /// </summary>
        public static async Task<List<BloodPacket>> AddCollectedPacketsAsync(
            AppDbContext context, Guid hospitalId, string bloodGroup, int count, DateTime collectionDate,
            string source, Guid? sourceReferenceId, string transactionType, string notes, Guid? performedByUserId)
        {
            if (count <= 0) return new List<BloodPacket>();

            var shelfLifeDays = await context.Hospitals
                .Where(h => h.HospitalId == hospitalId)
                .Select(h => (int?)h.PacketShelfLifeDays)
                .FirstOrDefaultAsync() ?? 35;

            var inventory = await GetOrCreateInventoryAsync(context, hospitalId, bloodGroup);
            var now = DateTime.UtcNow;
            var packets = new List<BloodPacket>();
            for (var i = 0; i < count; i++)
            {
                var packet = new BloodPacket
                {
                    PacketId = Guid.NewGuid(),
                    HospitalId = hospitalId,
                    BloodGroup = bloodGroup,
                    VolumeMl = BloodPacket.StandardVolumeMl,
                    CollectionDate = collectionDate,
                    ExpiryDate = collectionDate.AddDays(shelfLifeDays),
                    Status = BloodPacketStatus.Available,
                    Source = source,
                    SourceReferenceId = sourceReferenceId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                packets.Add(packet);
                await context.BloodPackets.AddAsync(packet);
                AddLedgerRow(context, inventory, transactionType, packet.PacketId, sourceReferenceId, performedByUserId, notes, now);
            }

            // UnitsAvailable counts every Available packet; the expiry sweep takes expired ones out
            Adjust(inventory, packets.Count, now);
            return packets;
        }

        /// <summary>Consumes the earliest-expiring available packets (first-expired, first-out).</summary>
        public static async Task<List<BloodPacket>> IssuePacketsAsync(
            AppDbContext context, Guid hospitalId, string bloodGroup, int count, Guid? referenceId, string notes, Guid? performedByUserId)
        {
            var packets = await TakeAvailablePacketsAsync(context, hospitalId, bloodGroup, count);
            var inventory = await GetOrCreateInventoryAsync(context, hospitalId, bloodGroup);
            var now = DateTime.UtcNow;
            foreach (var packet in packets)
            {
                packet.Status = BloodPacketStatus.Issued;
                Touch(packet, now);
                AddLedgerRow(context, inventory, TransactionType.Issued, packet.PacketId, referenceId, performedByUserId, notes, now);
            }
            Adjust(inventory, -packets.Count, now);
            return packets;
        }

        /// <summary>
        /// Moves packets between hospitals for a completed transfer. PacketIds and expiry dates stay the same;
        /// only the owner changes. Both hospitals get one audit row per packet.
        /// </summary>
        public static async Task<List<BloodPacket>> TransferPacketsAsync(
            AppDbContext context, Guid fromHospitalId, Guid toHospitalId, string bloodGroup, int count,
            Guid transferRequestId, string fromName, string toName, Guid? performedByUserId)
        {
            var packets = await TakeAvailablePacketsAsync(context, fromHospitalId, bloodGroup, count);
            var source = await GetOrCreateInventoryAsync(context, fromHospitalId, bloodGroup);
            var destination = await GetOrCreateInventoryAsync(context, toHospitalId, bloodGroup);
            var now = DateTime.UtcNow;
            foreach (var packet in packets)
            {
                packet.HospitalId = toHospitalId;
                Touch(packet, now);
                AddLedgerRow(context, source, TransactionType.TransferOut, packet.PacketId, transferRequestId, performedByUserId,
                    $"Transferred to {toName}", now);
                AddLedgerRow(context, destination, TransactionType.TransferIn, packet.PacketId, transferRequestId, performedByUserId,
                    $"Received from {fromName}", now);
            }
            Adjust(source, -packets.Count, now);
            Adjust(destination, packets.Count, now);
            return packets;
        }

        /// <summary>Marks available packets past their expiry date as Expired (system action).</summary>
        public static async Task<int> ExpirePacketsAsync(AppDbContext context, DateTime now)
        {
            var expired = await context.BloodPackets
                .Where(p => p.Status == BloodPacketStatus.Available && p.ExpiryDate <= now)
                .ToListAsync();
            foreach (var group in expired.GroupBy(p => new { p.HospitalId, p.BloodGroup }))
            {
                var inventory = await GetOrCreateInventoryAsync(context, group.Key.HospitalId, group.Key.BloodGroup);
                foreach (var packet in group)
                {
                    packet.Status = BloodPacketStatus.Expired;
                    Touch(packet, now);
                    AddLedgerRow(context, inventory, TransactionType.Expired, packet.PacketId, null, null,
                        $"Packet expired on {packet.ExpiryDate:yyyy-MM-dd}", now);
                }
                Adjust(inventory, -group.Count(), now);
            }
            return expired.Count;
        }

        public static Task<int> CountAvailableAsync(AppDbContext context, Guid hospitalId, string bloodGroup)
        {
            var now = DateTime.UtcNow;
            return context.BloodPackets.CountAsync(p => p.HospitalId == hospitalId && p.BloodGroup == bloodGroup &&
                                                        p.Status == BloodPacketStatus.Available && p.ExpiryDate > now);
        }

        private static async Task<List<BloodPacket>> TakeAvailablePacketsAsync(AppDbContext context, Guid hospitalId, string bloodGroup, int count)
        {
            if (count <= 0)
            {
                throw new InvalidOperationException("Units must be greater than zero.");
            }

            var now = DateTime.UtcNow;
            var packets = await context.BloodPackets
                .Where(p => p.HospitalId == hospitalId && p.BloodGroup == bloodGroup &&
                            p.Status == BloodPacketStatus.Available && p.ExpiryDate > now)
                .OrderBy(p => p.ExpiryDate)
                .ThenBy(p => p.CollectionDate)
                .Take(count)
                .ToListAsync();

            if (packets.Count < count)
            {
                throw new InvalidOperationException(
                    $"Only {packets.Count} unexpired {bloodGroup} packet(s) are available; {count} requested.");
            }
            return packets;
        }

        private static void AddLedgerRow(AppDbContext context, BloodInventory inventory, string type, Guid packetId,
            Guid? referenceId, Guid? performedByUserId, string notes, DateTime now)
        {
            context.InventoryTransactions.Add(new InventoryTransaction
            {
                TransactionId = Guid.NewGuid(),
                InventoryId = inventory.InventoryId,
                TransactionType = type,
                Units = 1,
                PacketId = packetId,
                ReferenceId = referenceId,
                PerformedByUserId = performedByUserId,
                Notes = notes,
                CreatedAt = now
            });
        }

        private static void Adjust(BloodInventory inventory, int delta, DateTime now)
        {
            inventory.UnitsAvailable = Math.Max(0, inventory.UnitsAvailable + delta);
            inventory.ConcurrencyToken++;
            inventory.LastUpdated = now;
            inventory.UpdatedAt = now;
        }

        private static void Touch(BloodPacket packet, DateTime now)
        {
            packet.ConcurrencyToken++;
            packet.UpdatedAt = now;
        }
    }
}
