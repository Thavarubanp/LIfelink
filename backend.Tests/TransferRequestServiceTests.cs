using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Transfer;
using LifeLink.Entities;
using LifeLink.Services.Inventory;
using LifeLink.Services.Transfer;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeLink.Tests
{
    public class TransferRequestServiceTests
    {
        private static async Task<(AppDbContext Context, Hospital A, Hospital B)> SeedAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            var context = new AppDbContext(options);
            context.Database.EnsureCreated();

            var a = new Hospital { HospitalId = Guid.NewGuid(), Name = "Hospital A", Email = "a@h.org", IsVerified = true };
            var b = new Hospital { HospitalId = Guid.NewGuid(), Name = "Hospital B", Email = "b@h.org", IsVerified = true };
            await context.Hospitals.AddRangeAsync(a, b);
            await context.SaveChangesAsync();
            return (context, a, b);
        }

        private static async Task AddStockAsync(AppDbContext context, Guid hospitalId, string group, int units)
        {
            await InventoryLedger.AddCollectedPacketsAsync(context, hospitalId, group, units, DateTime.UtcNow,
                BloodPacketSource.Seed, null, TransactionType.Seeded, "test stock", null);
            await context.SaveChangesAsync();
        }

        private static TransferRequestCreateDto Dto(string type, Guid counterpart, string group = "O+", int units = 2, List<Guid>? packetIds = null) =>
            new() { TransferType = type, CounterpartHospitalId = counterpart, BloodGroup = group, UnitsRequested = units, Notes = "test", PacketIds = packetIds };

        private static Task<List<Guid>> PacketIdsAsync(AppDbContext context, Guid hospitalId, int take, string group = "O+") =>
            context.BloodPackets.Where(p => p.HospitalId == hospitalId && p.BloodGroup == group && p.Status == BloodPacketStatus.Available)
                .OrderBy(p => p.TrackingNumber).Take(take).Select(p => p.PacketId).ToListAsync();

        private static async Task<int> UnitsAsync(AppDbContext context, Guid hospitalId, string group = "O+") =>
            (await context.BloodInventories.SingleOrDefaultAsync(i => i.HospitalId == hospitalId && i.BloodGroup == group))?.UnitsAvailable ?? 0;

        [Fact]
        public async Task CreateTransferRequestAsync_SameSenderAndReceiver_ThrowsInvalidOperationException()
        {
            var (context, a, _) = await SeedAsync();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new TransferRequestService(context).CreateTransferRequestAsync(Dto("Request", a.HospitalId), a.HospitalId));
            Assert.Contains("same", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CreateTransferRequestAsync_ZeroUnits_ThrowsInvalidOperationException()
        {
            var (context, a, b) = await SeedAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new TransferRequestService(context).CreateTransferRequestAsync(Dto("Request", b.HospitalId, units: 0), a.HospitalId));
        }

        [Fact]
        public async Task Request_Is_Created_By_Receiver_And_Offer_By_Sender()
        {
            var (context, a, b) = await SeedAsync();
            var service = new TransferRequestService(context);
            await AddStockAsync(context, a.HospitalId, "O+", 3);

            var request = await service.CreateTransferRequestAsync(Dto("Request", b.HospitalId), a.HospitalId);
            Assert.Equal(b.HospitalId, request.SenderHospitalId);
            Assert.Equal(a.HospitalId, request.ReceiverHospitalId);
            Assert.Equal(a.HospitalId, request.CreatedByHospitalId);
            Assert.Equal("Pending", request.Status);

            var offer = await service.CreateTransferRequestAsync(Dto("Offer", b.HospitalId, packetIds: await PacketIdsAsync(context, a.HospitalId, 2)), a.HospitalId);
            Assert.Equal(a.HospitalId, offer.SenderHospitalId);
            Assert.Equal(b.HospitalId, offer.ReceiverHospitalId);
            Assert.Equal(1, await context.Notifications.CountAsync(n => n.HospitalId == b.HospitalId && n.NotificationType == "TransferOffered"));
        }

        [Fact]
        public async Task Offer_Holds_The_Selected_Packets_Until_Rejected_Or_Withdrawn()
        {
            var (context, a, b) = await SeedAsync();
            var service = new TransferRequestService(context);
            await AddStockAsync(context, a.HospitalId, "O+", 3);
            await AddStockAsync(context, b.HospitalId, "O+", 1);

            // Packets must be chosen, and must be the offering hospital's own
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateTransferRequestAsync(Dto("Offer", b.HospitalId), a.HospitalId));
            var foreignPackets = await PacketIdsAsync(context, b.HospitalId, 1);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateTransferRequestAsync(Dto("Offer", b.HospitalId, packetIds: foreignPackets), a.HospitalId));

            var chosen = await PacketIdsAsync(context, a.HospitalId, 2);
            var offer = await service.CreateTransferRequestAsync(Dto("Offer", b.HospitalId, units: 99, packetIds: chosen), a.HospitalId);
            Assert.Equal(2, offer.UnitsRequested); // units = packets selected
            Assert.Equal(1, await UnitsAsync(context, a.HospitalId));
            Assert.All(chosen, id => Assert.Equal(BloodPacketStatus.Reserved, context.BloodPackets.Find(id)!.Status));
            Assert.Equal(2, (await service.GetTransferRequestAsync(offer.TransferRequestId))!.PacketTrackingNumbers.Count);

            // Held packets cannot be offered again
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateTransferRequestAsync(Dto("Offer", b.HospitalId, packetIds: chosen), a.HospitalId));

            await service.RejectTransferRequestAsync(offer.TransferRequestId, b.HospitalId, "Not needed");
            Assert.Equal(3, await UnitsAsync(context, a.HospitalId));
            Assert.All(chosen, id => Assert.Equal(BloodPacketStatus.Available, context.BloodPackets.Find(id)!.Status));

            var second = await service.CreateTransferRequestAsync(Dto("Offer", b.HospitalId, packetIds: chosen), a.HospitalId);
            Assert.Equal(1, await UnitsAsync(context, a.HospitalId));
            await service.DeleteTransferRequestAsync(second.TransferRequestId, a.HospitalId);
            Assert.Equal(3, await UnitsAsync(context, a.HospitalId));
        }

        [Fact]
        public async Task Accepting_An_Offer_Moves_The_Held_Packets_With_All_Their_Details()
        {
            var (context, a, b) = await SeedAsync();
            var service = new TransferRequestService(context);
            await AddStockAsync(context, a.HospitalId, "O+", 3);
            var chosen = await PacketIdsAsync(context, a.HospitalId, 2);
            var before = await context.BloodPackets.AsNoTracking().Where(p => chosen.Contains(p.PacketId)).ToListAsync();

            var offer = await service.CreateTransferRequestAsync(Dto("Offer", b.HospitalId, packetIds: chosen), a.HospitalId);
            var completed = await service.ApproveTransferRequestAsync(offer.TransferRequestId, b.HospitalId);

            Assert.Equal("Completed", completed.Status);
            Assert.Equal(before.Select(p => p.TrackingNumber).OrderBy(n => n), completed.PacketTrackingNumbers);
            foreach (var old in before)
            {
                var now = await context.BloodPackets.FindAsync(old.PacketId);
                Assert.Equal(b.HospitalId, now!.HospitalId);                  // receiver owns it
                Assert.Equal(BloodPacketStatus.Available, now.Status);
                Assert.Equal(old.TrackingNumber, now.TrackingNumber);          // everything else unchanged
                Assert.Equal(a.HospitalId, now.CreatedByHospitalId);
                Assert.Equal(old.BloodGroup, now.BloodGroup);
                Assert.Equal(old.CollectionDate, now.CollectionDate);
                Assert.Equal(old.CreatedAt, now.CreatedAt);
            }

            Assert.Equal(1, await UnitsAsync(context, a.HospitalId));
            Assert.Equal(2, await UnitsAsync(context, b.HospitalId));
            // The sender no longer lists them; the receiver lists them as its own Available packets
            var inventory = new BloodInventoryService(context);
            Assert.DoesNotContain(await inventory.GetPacketsAsync(a.HospitalId, null, null, null, a.HospitalId), p => chosen.Contains(p.PacketId));
            Assert.Equal(2, (await inventory.GetPacketsAsync(b.HospitalId, null, BloodPacketStatus.Available, null, b.HospitalId)).Count());
        }

        [Fact]
        public async Task An_Offer_Created_Before_Packet_Selection_Cannot_Be_Accepted()
        {
            var (context, a, b) = await SeedAsync();
            await AddStockAsync(context, a.HospitalId, "O+", 3);
            var legacy = new HospitalTransferRequest
            {
                TransferRequestId = Guid.NewGuid(), SenderHospitalId = a.HospitalId, ReceiverHospitalId = b.HospitalId, BloodGroup = "O+",
                UnitsRequested = 2, TransferType = TransferTypes.Offer, Status = TransferRequestStatus.Pending.ToString()
            };
            await context.HospitalTransferRequests.AddAsync(legacy);
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new TransferRequestService(context).ApproveTransferRequestAsync(legacy.TransferRequestId, b.HospitalId));
            Assert.Contains("no reserved packets", ex.Message);
            Assert.Equal(3, await UnitsAsync(context, a.HospitalId));
        }

        [Fact]
        public async Task Suspended_Or_Unapproved_Hospitals_Cannot_Take_Part()
        {
            var (context, a, b) = await SeedAsync();
            b.IsSuspended = true;
            var unapproved = new Hospital { HospitalId = Guid.NewGuid(), Name = "Pending", Email = "p@h.org", IsVerified = false };
            await context.Hospitals.AddAsync(unapproved);
            await context.SaveChangesAsync();
            var service = new TransferRequestService(context);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateTransferRequestAsync(Dto("Request", b.HospitalId), a.HospitalId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateTransferRequestAsync(Dto("Request", unapproved.HospitalId), a.HospitalId));
        }

        [Fact]
        public async Task Accepting_Moves_The_Same_Packets_To_The_Receiver_And_Audits_Each_Move()
        {
            var (context, a, b) = await SeedAsync();
            var service = new TransferRequestService(context);
            await AddStockAsync(context, b.HospitalId, "O+", 5);
            var packetIdsBefore = await context.BloodPackets.Where(p => p.HospitalId == b.HospitalId).Select(p => p.PacketId).ToListAsync();

            // A requests 2 units from B; only B (the counterpart) can accept, choosing exactly 2 of its packets
            var created = await service.CreateTransferRequestAsync(Dto("Request", b.HospitalId), a.HospitalId);
            var chosen = await context.BloodPackets.Where(p => p.HospitalId == b.HospitalId)
                .OrderByDescending(p => p.TrackingNumber).Take(2).Select(p => p.PacketId).ToListAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApproveTransferRequestAsync(created.TransferRequestId, a.HospitalId, null, chosen));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveTransferRequestAsync(created.TransferRequestId, b.HospitalId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveTransferRequestAsync(created.TransferRequestId, b.HospitalId, null, chosen.Take(1).ToList()));

            var completed = await service.ApproveTransferRequestAsync(created.TransferRequestId, b.HospitalId, null, chosen);

            Assert.Equal("Completed", completed.Status);
            Assert.Equal(2, completed.PacketIds.Count);
            Assert.Equal(chosen.OrderBy(x => x), completed.PacketIds.OrderBy(x => x)); // exactly the packets the sender chose
            Assert.All(completed.PacketIds, id => Assert.Contains(id, packetIdsBefore)); // packet IDs unchanged
            Assert.Equal(2, await context.BloodPackets.CountAsync(p => p.HospitalId == a.HospitalId && completed.PacketIds.Contains(p.PacketId)));

            var inventoryA = await context.BloodInventories.SingleAsync(i => i.HospitalId == a.HospitalId && i.BloodGroup == "O+");
            var inventoryB = await context.BloodInventories.SingleAsync(i => i.HospitalId == b.HospitalId && i.BloodGroup == "O+");
            Assert.Equal(2, inventoryA.UnitsAvailable);
            Assert.Equal(3, inventoryB.UnitsAvailable);

            foreach (var packetId in completed.PacketIds)
            {
                Assert.Single(context.InventoryTransactions.Where(t => t.PacketId == packetId && t.TransactionType == TransactionType.TransferOut && t.ReferenceId == created.TransferRequestId));
                Assert.Single(context.InventoryTransactions.Where(t => t.PacketId == packetId && t.TransactionType == TransactionType.TransferIn && t.ReferenceId == created.TransferRequestId));
            }
        }

        [Fact]
        public async Task Accepting_Without_Enough_Stock_Changes_Nothing()
        {
            var (context, a, b) = await SeedAsync();
            var service = new TransferRequestService(context);
            await AddStockAsync(context, b.HospitalId, "O+", 1);
            var created = await service.CreateTransferRequestAsync(Dto("Request", b.HospitalId, units: 3), a.HospitalId);

            var onePacket = await PacketIdsAsync(context, b.HospitalId, 1);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ApproveTransferRequestAsync(created.TransferRequestId, b.HospitalId, null, onePacket));
            Assert.Contains("exactly 3", ex.Message);
            Assert.Equal("Pending", (await context.HospitalTransferRequests.FindAsync(created.TransferRequestId))!.Status);
            Assert.Equal(1, await context.BloodPackets.CountAsync(p => p.HospitalId == b.HospitalId));
        }

        [Fact]
        public async Task Rejection_Requires_A_Reason_And_Is_Kept()
        {
            var (context, a, b) = await SeedAsync();
            var service = new TransferRequestService(context);
            var created = await service.CreateTransferRequestAsync(Dto("Request", b.HospitalId), a.HospitalId);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RejectTransferRequestAsync(created.TransferRequestId, b.HospitalId, " "));
            var rejected = await service.RejectTransferRequestAsync(created.TransferRequestId, b.HospitalId, "No spare stock");

            Assert.Equal("Rejected", rejected.Status);
            Assert.Equal("No spare stock", rejected.RejectionReason);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveTransferRequestAsync(created.TransferRequestId, b.HospitalId));
        }

        [Fact]
        public async Task Only_The_Creator_Can_Delete_A_Pending_Transfer_And_History_Is_Kept()
        {
            var (context, a, b) = await SeedAsync();
            var service = new TransferRequestService(context);
            var created = await service.CreateTransferRequestAsync(Dto("Request", b.HospitalId), a.HospitalId);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteTransferRequestAsync(created.TransferRequestId, b.HospitalId));
            var deleted = await service.DeleteTransferRequestAsync(created.TransferRequestId, a.HospitalId);

            Assert.Equal("Cancelled", deleted.Status);
            Assert.NotNull(await context.HospitalTransferRequests.FindAsync(created.TransferRequestId));
            Assert.Empty(await service.GetPendingTransferRequestsAsync(a.HospitalId));
            Assert.Single(await service.GetAllTransferRequestsAsync(b.HospitalId)); // counterpart still sees it in history
        }

        [Fact]
        public async Task GetPendingTransferRequestsAsync_ReturnsOnlyPendingTransfers_For_The_Hospital()
        {
            var (context, a, b) = await SeedAsync();
            var service = new TransferRequestService(context);
            await AddStockAsync(context, b.HospitalId, "B-", 4);

            var pending = await service.CreateTransferRequestAsync(Dto("Request", b.HospitalId, "O+", 10), a.HospitalId);
            var toAccept = await service.CreateTransferRequestAsync(Dto("Request", b.HospitalId, "B-", 4), a.HospitalId);
            await service.ApproveTransferRequestAsync(toAccept.TransferRequestId, b.HospitalId, null, await PacketIdsAsync(context, b.HospitalId, 4, "B-"));

            var pendingList = (await service.GetPendingTransferRequestsAsync(a.HospitalId)).ToList();
            Assert.Single(pendingList);
            Assert.Equal(pending.TransferRequestId, pendingList.First().TransferRequestId);
        }
    }
}
