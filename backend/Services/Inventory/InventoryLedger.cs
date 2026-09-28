using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Inventory
{
    /// <summary>
    /// The only place that creates, moves or consumes blood packets. Every packet change writes one
    /// InventoryTransaction row (PacketId + ReferenceId + actor) and adjusts BloodInventory.UnitsAvailable in the
    /// same unit of work, so UnitsAvailable always equals the hospital's Available packets of that group. Callers call
    /// SaveChanges once (SavePacketChangesAsync), so stock and audit trail commit together. Staff always choose the
    /// packets by ID; concurrency tokens on packets and inventory rows make a second use of the same packet fail.
    /// </summary>
    public static class InventoryLedger
    {
        public const string PacketConflictMessage = "One or more selected packets were just used elsewhere. Refresh and select again.";

        private static long _nonRelationalTrackingCounter; // in-memory test databases have no sequences

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
        /// Next tracking numbers (PKT-00001234) from the database sequence. nextval is atomic, so two packets created
        /// at the same moment never get the same number; the unique index on TrackingNumber backs this up.
        /// </summary>
        public static async Task<List<string>> NextTrackingNumbersAsync(AppDbContext context, int count)
        {
            if (count <= 0) return new List<string>();

            List<long> values;
            if (context.Database.IsRelational())
            {
                values = await context.Database
                    .SqlQueryRaw<long>($"SELECT nextval('\"{AppDbContext.BloodPacketTrackingSequence}\"') AS \"Value\" FROM generate_series(1, {count})")
                    .ToListAsync();
            }
            else
            {
                values = Enumerable.Range(0, count).Select(_ => Interlocked.Increment(ref _nonRelationalTrackingCounter)).ToList();
            }

            return values.Select(FormatTrackingNumber).ToList();
        }

        public static string FormatTrackingNumber(long value) => $"PKT-{value:D8}";

        /// <summary>
        /// Creates packets for collected blood: a recorded donation, hospital staff entering collected stock, or the
        /// Development seeder. The creating hospital owns them; expiry follows its shelf-life setting.
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
            var trackingNumbers = await NextTrackingNumbersAsync(context, count);
            var now = DateTime.UtcNow;
            var packets = new List<BloodPacket>();
            for (var i = 0; i < count; i++)
            {
                var packet = new BloodPacket
                {
                    PacketId = Guid.NewGuid(),
                    TrackingNumber = trackingNumbers[i],
                    CreatedByHospitalId = hospitalId,
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

        /// <summary>
        /// Loads the packets a hospital selected and checks they can be used now: each one listed once, owned by the
        /// hospital, Available, not expired, and of the expected blood group and count (when given).
        /// </summary>
        public static async Task<List<BloodPacket>> RequireSelectablePacketsAsync(
            AppDbContext context, Guid hospitalId, IReadOnlyCollection<Guid>? packetIds, string? bloodGroup = null, int? requiredCount = null)
        {
            if (packetIds == null || packetIds.Count == 0)
            {
                throw new InvalidOperationException("Select at least one blood packet.");
            }

            if (packetIds.Distinct().Count() != packetIds.Count)
            {
                throw new InvalidOperationException("The same packet was selected more than once.");
            }

            if (requiredCount.HasValue && packetIds.Count != requiredCount.Value)
            {
                throw new InvalidOperationException($"Select exactly {requiredCount.Value} packet(s); {packetIds.Count} selected.");
            }

            var ids = packetIds.ToList();
            var packets = await context.BloodPackets.Where(p => ids.Contains(p.PacketId)).ToListAsync();
            if (packets.Count != ids.Count || packets.Any(p => p.HospitalId != hospitalId))
            {
                throw new InvalidOperationException("One or more selected packets are not in your hospital's inventory.");
            }

            var now = DateTime.UtcNow;
            foreach (var packet in packets)
            {
                if (packet.Status != BloodPacketStatus.Available)
                {
                    throw new InvalidOperationException($"Packet {packet.TrackingNumber} is {packet.Status} and cannot be used.");
                }
                if (packet.ExpiryDate <= now)
                {
                    throw new InvalidOperationException($"Packet {packet.TrackingNumber} has expired and cannot be used.");
                }
                if (bloodGroup != null && !string.Equals(packet.BloodGroup, bloodGroup, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Packet {packet.TrackingNumber} is {packet.BloodGroup}; only {bloodGroup} packets can be selected.");
                }
            }

            // Keep the caller's order (stable audit rows)
            return ids.Select(id => packets.First(p => p.PacketId == id)).ToList();
        }

        /// <summary>Issues the selected packets (kept with status Issued; they leave the available count).</summary>
        public static async Task IssuePacketsAsync(AppDbContext context, IEnumerable<BloodPacket> packets, Guid? referenceId, string notes, Guid? performedByUserId)
        {
            var now = DateTime.UtcNow;
            foreach (var group in packets.GroupBy(p => new { p.HospitalId, p.BloodGroup }))
            {
                var inventory = await GetOrCreateInventoryAsync(context, group.Key.HospitalId, group.Key.BloodGroup);
                foreach (var packet in group)
                {
                    packet.Status = BloodPacketStatus.Issued;
                    Touch(packet, now);
                    AddLedgerRow(context, inventory, TransactionType.Issued, packet.PacketId, referenceId, performedByUserId, notes, now);
                }
                Adjust(inventory, -group.Count(), now);
            }
        }

        /// <summary>
        /// Holds Available packets for a pending transfer offer or hospital donation (status Reserved). They leave the
        /// available count so they cannot be used twice, and return when the hold is released.
        /// </summary>
        public static async Task ReservePacketsAsync(AppDbContext context, IEnumerable<BloodPacket> packets, Guid holderId, string notes, Guid? performedByUserId)
        {
            var now = DateTime.UtcNow;
            foreach (var group in packets.GroupBy(p => new { p.HospitalId, p.BloodGroup }))
            {
                var inventory = await GetOrCreateInventoryAsync(context, group.Key.HospitalId, group.Key.BloodGroup);
                foreach (var packet in group)
                {
                    packet.Status = BloodPacketStatus.Reserved;
                    packet.HeldForReferenceId = holderId;
                    Touch(packet, now);
                    AddLedgerRow(context, inventory, TransactionType.Reserved, packet.PacketId, holderId, performedByUserId, notes, now);
                }
                Adjust(inventory, -group.Count(), now);
            }
        }

        public static Task<List<BloodPacket>> GetHeldPacketsAsync(AppDbContext context, Guid holderId) =>
            context.BloodPackets
                .Where(p => p.HeldForReferenceId == holderId && p.Status == BloodPacketStatus.Reserved)
                .OrderBy(p => p.TrackingNumber)
                .ToListAsync();

        /// <summary>
        /// Ends a hold: packets return to Available (or become Expired if their expiry date passed while held).
        /// </summary>
        public static async Task<List<BloodPacket>> ReleaseHeldPacketsAsync(AppDbContext context, Guid holderId, string notes, Guid? performedByUserId)
        {
            var packets = await GetHeldPacketsAsync(context, holderId);
            var now = DateTime.UtcNow;
            foreach (var group in packets.GroupBy(p => new { p.HospitalId, p.BloodGroup }))
            {
                var inventory = await GetOrCreateInventoryAsync(context, group.Key.HospitalId, group.Key.BloodGroup);
                var returned = 0;
                foreach (var packet in group)
                {
                    packet.HeldForReferenceId = null;
                    Touch(packet, now);
                    if (packet.ExpiryDate <= now)
                    {
                        packet.Status = BloodPacketStatus.Expired;
                        AddLedgerRow(context, inventory, TransactionType.Expired, packet.PacketId, holderId, performedByUserId,
                            $"Hold ended after the packet expired on {packet.ExpiryDate:yyyy-MM-dd}", now);
                        continue;
                    }
                    packet.Status = BloodPacketStatus.Available;
                    AddLedgerRow(context, inventory, TransactionType.Released, packet.PacketId, holderId, performedByUserId, notes, now);
                    returned++;
                }
                Adjust(inventory, returned, now);
            }
            return packets;
        }

        /// <summary>
        /// Moves packets to another hospital. Every packet field (tracking number, creator, group, collected and created
        /// dates) stays the same; only the owner changes and the packet is Available there. Works for Available
        /// packets (sender chose them now) and Reserved ones (held by the offer or donation being completed).
        /// </summary>
        public static async Task MovePacketsAsync(
            AppDbContext context, IEnumerable<BloodPacket> packets, Guid toHospitalId, Guid referenceId,
            string outType, string inType, string outNotes, string inNotes, Guid? performedByUserId)
        {
            var now = DateTime.UtcNow;
            foreach (var packet in packets.ToList())
            {
                var source = await GetOrCreateInventoryAsync(context, packet.HospitalId, packet.BloodGroup);
                var destination = await GetOrCreateInventoryAsync(context, toHospitalId, packet.BloodGroup);
                if (packet.Status == BloodPacketStatus.Available)
                {
                    Adjust(source, -1, now); // reserved packets already left the sender's count
                }

                packet.HospitalId = toHospitalId;
                packet.Status = BloodPacketStatus.Available;
                packet.HeldForReferenceId = null;
                Touch(packet, now);
                AddLedgerRow(context, source, outType, packet.PacketId, referenceId, performedByUserId, outNotes, now);
                AddLedgerRow(context, destination, inType, packet.PacketId, referenceId, performedByUserId, inNotes, now);
                Adjust(destination, 1, now);
            }
        }

        /// <summary>Held packets given to a patient's blood request: kept with status Donated at the donating hospital.</summary>
        public static async Task DonateHeldPacketsAsync(AppDbContext context, IEnumerable<BloodPacket> packets, Guid referenceId, string notes, Guid? performedByUserId)
        {
            var now = DateTime.UtcNow;
            foreach (var packet in packets)
            {
                var inventory = await GetOrCreateInventoryAsync(context, packet.HospitalId, packet.BloodGroup);
                packet.Status = BloodPacketStatus.Donated;
                packet.HeldForReferenceId = null;
                Touch(packet, now);
                AddLedgerRow(context, inventory, TransactionType.Donated, packet.PacketId, referenceId, performedByUserId, notes, now);
            }
        }

        /// <summary>
        /// Corrects an Available packet's blood group and/or collected date (expiry follows the collected date).
        /// A group change moves the unit between the two blood group counts.
        /// </summary>
        public static async Task ChangePacketDetailsAsync(AppDbContext context, BloodPacket packet, string bloodGroup, DateTime collectionDate, int shelfLifeDays, Guid? performedByUserId)
        {
            var now = DateTime.UtcNow;
            var changes = new List<string>();
            var fromInventory = await GetOrCreateInventoryAsync(context, packet.HospitalId, packet.BloodGroup);
            var toInventory = fromInventory;

            if (!string.Equals(packet.BloodGroup, bloodGroup, StringComparison.Ordinal))
            {
                toInventory = await GetOrCreateInventoryAsync(context, packet.HospitalId, bloodGroup);
                changes.Add($"blood group {packet.BloodGroup} -> {bloodGroup}");
                Adjust(fromInventory, -1, now);
                Adjust(toInventory, 1, now);
                packet.BloodGroup = bloodGroup;
            }

            if (packet.CollectionDate != collectionDate)
            {
                changes.Add($"collected date {packet.CollectionDate:yyyy-MM-dd} -> {collectionDate:yyyy-MM-dd}");
                packet.CollectionDate = collectionDate;
                packet.ExpiryDate = collectionDate.AddDays(shelfLifeDays);
            }

            if (changes.Count == 0) return;

            Touch(packet, now);
            AddLedgerRow(context, toInventory, TransactionType.Adjustment, packet.PacketId, null, performedByUserId,
                $"Packet {packet.TrackingNumber} edited: {string.Join("; ", changes)}", now);
        }

        /// <summary>Marks available packets past their expiry date as Expired (system action), optionally for one stock row.</summary>
        public static async Task<int> ExpirePacketsAsync(AppDbContext context, DateTime now, Guid? hospitalId = null, string? bloodGroup = null)
        {
            var expired = await context.BloodPackets
                .Where(p => p.Status == BloodPacketStatus.Available && p.ExpiryDate <= now &&
                            (hospitalId == null || p.HospitalId == hospitalId) &&
                            (bloodGroup == null || p.BloodGroup == bloodGroup))
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

        /// <summary>
        /// Saves packet changes; a concurrent change to the same packet or stock row (someone else used it first), or to
        /// another versioned record in the same save, becomes a ConflictException (HTTP 409) and nothing is saved.
        /// </summary>
        public static async Task SavePacketChangesAsync(AppDbContext context, string conflictMessage = PacketConflictMessage)
        {
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConflictException(conflictMessage);
            }
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
            if (delta == 0) return;
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
