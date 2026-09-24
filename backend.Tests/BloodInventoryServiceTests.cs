using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Inventory;
using LifeLink.Entities;
using LifeLink.Services.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeLink.Tests
{
    public class BloodInventoryServiceTests
    {
        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        private static async Task<Guid> AddHospitalAsync(AppDbContext context, int alertDays = 5)
        {
            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Test Hospital", Email = $"{Guid.NewGuid():N}@h.org", IsVerified = true, ExpiryAlertDays = alertDays };
            await context.Hospitals.AddAsync(hospital);
            await context.SaveChangesAsync();
            return hospital.HospitalId;
        }

        // Stock only ever arrives as packets (donations / transfers); tests use the seeder path of the ledger
        private static async Task AddPacketsAsync(AppDbContext context, Guid hospitalId, string group, int count, DateTime? collected = null)
        {
            await InventoryLedger.AddCollectedPacketsAsync(context, hospitalId, group, count, collected ?? DateTime.UtcNow,
                BloodPacketSource.Seed, null, TransactionType.Seeded, "test stock", null);
            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task CreateInventoryAsync_Creates_An_Empty_Category_With_Thresholds()
        {
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = await AddHospitalAsync(context);

            var result = await service.CreateInventoryAsync(new CreateInventoryDto
            {
                HospitalId = hospitalId,
                BloodGroup = "A+",
                MinimumThreshold = 10,
                MaximumCapacity = 100
            });

            Assert.Equal(hospitalId, result.HospitalId);
            Assert.Equal("A+", result.BloodGroup);
            Assert.Equal(0, result.UnitsAvailable);
            Assert.Equal(10, result.MinimumThreshold);
            Assert.Empty(await service.GetInventoryTransactionsAsync(result.InventoryId));
        }

        [Fact]
        public async Task CreateInventoryAsync_NegativeThreshold_ThrowsInvalidOperationException()
        {
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = await AddHospitalAsync(context);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInventoryAsync(new CreateInventoryDto
            {
                HospitalId = hospitalId,
                BloodGroup = "A+",
                MinimumThreshold = -1,
                MaximumCapacity = 100
            }));
        }

        [Fact]
        public async Task CreateInventoryAsync_InvalidBloodGroup_ThrowsInvalidOperationException()
        {
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = await AddHospitalAsync(context);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInventoryAsync(new CreateInventoryDto
            {
                HospitalId = hospitalId,
                BloodGroup = "X+",
                MinimumThreshold = 10,
                MaximumCapacity = 100
            }));
        }

        [Fact]
        public async Task CreateInventoryAsync_DuplicateHospitalAndBloodGroup_ThrowsInvalidOperationException()
        {
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = await AddHospitalAsync(context);
            var dto = new CreateInventoryDto { HospitalId = hospitalId, BloodGroup = "B+", MinimumThreshold = 5, MaximumCapacity = 50 };

            await service.CreateInventoryAsync(dto);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInventoryAsync(dto));
            Assert.Contains("already exists", ex.Message);
        }

        [Fact]
        public async Task UpdateInventoryAsync_Cannot_Raise_Stock_By_Hand()
        {
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = await AddHospitalAsync(context);
            await AddPacketsAsync(context, hospitalId, "O-", 2);
            var inventory = await context.BloodInventories.SingleAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateInventoryAsync(inventory.InventoryId,
                new UpdateInventoryDto { UnitsAvailable = 10, MinimumThreshold = 1, MaximumCapacity = 100, AuditNotes = "Restock" }));
            Assert.Contains("donations or completed hospital transfers", ex.Message);
        }

        [Fact]
        public async Task UpdateInventoryAsync_Lower_Count_Issues_Earliest_Expiring_Packets_With_A_Reason()
        {
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = await AddHospitalAsync(context);
            await AddPacketsAsync(context, hospitalId, "O-", 1, DateTime.UtcNow.AddDays(-20)); // expires first
            await AddPacketsAsync(context, hospitalId, "O-", 2);
            var inventory = await context.BloodInventories.SingleAsync();
            var oldest = await context.BloodPackets.OrderBy(p => p.ExpiryDate).FirstAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateInventoryAsync(inventory.InventoryId,
                new UpdateInventoryDto { UnitsAvailable = 2, MinimumThreshold = 1, MaximumCapacity = 100 }));

            var updated = await service.UpdateInventoryAsync(inventory.InventoryId,
                new UpdateInventoryDto { UnitsAvailable = 2, MinimumThreshold = 4, MaximumCapacity = 100, AuditNotes = "Issued to theatre" });

            Assert.Equal(2, updated.UnitsAvailable);
            Assert.Equal(4, updated.MinimumThreshold);
            Assert.Equal(BloodPacketStatus.Issued, (await context.BloodPackets.FindAsync(oldest.PacketId))!.Status);
            var issued = await context.InventoryTransactions.SingleAsync(t => t.TransactionType == TransactionType.Issued);
            Assert.Equal(oldest.PacketId, issued.PacketId);
            Assert.Equal("Issued to theatre", issued.Notes);
        }

        [Fact]
        public async Task GetLowStockInventoryAsync_ReturnsOnlyLowStock()
        {
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = await AddHospitalAsync(context);
            await service.CreateInventoryAsync(new CreateInventoryDto { HospitalId = hospitalId, BloodGroup = "A+", MinimumThreshold = 10, MaximumCapacity = 100 });
            await service.CreateInventoryAsync(new CreateInventoryDto { HospitalId = hospitalId, BloodGroup = "B+", MinimumThreshold = 2, MaximumCapacity = 100 });
            await AddPacketsAsync(context, hospitalId, "A+", 5);
            await AddPacketsAsync(context, hospitalId, "B+", 5);

            var lowStock = (await service.GetLowStockInventoryAsync()).ToList();

            Assert.Single(lowStock);
            Assert.Equal("A+", lowStock.First().BloodGroup);
        }

        [Fact]
        public async Task GetSurplusInventoryAsync_ReturnsOnlySurplusStock()
        {
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = await AddHospitalAsync(context);
            await service.CreateInventoryAsync(new CreateInventoryDto { HospitalId = hospitalId, BloodGroup = "O+", MinimumThreshold = 1, MaximumCapacity = 10 });
            await service.CreateInventoryAsync(new CreateInventoryDto { HospitalId = hospitalId, BloodGroup = "A-", MinimumThreshold = 1, MaximumCapacity = 10 });
            await AddPacketsAsync(context, hospitalId, "O+", 9);
            await AddPacketsAsync(context, hospitalId, "A-", 3);

            var surplus = (await service.GetSurplusInventoryAsync()).ToList();

            Assert.Single(surplus);
            Assert.Equal("O+", surplus.First().BloodGroup);
        }

        [Fact]
        public async Task Expiring_Packets_Are_Counted_And_Expired_Ones_Are_Swept()
        {
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = await AddHospitalAsync(context, alertDays: 5);
            await AddPacketsAsync(context, hospitalId, "AB+", 1, DateTime.UtcNow.AddDays(-32)); // expires in ~3 days
            await AddPacketsAsync(context, hospitalId, "AB+", 1, DateTime.UtcNow.AddDays(-40)); // already expired
            await AddPacketsAsync(context, hospitalId, "AB+", 1);

            var before = (await service.GetHospitalInventoryAsync(hospitalId)).Single();
            Assert.Equal(3, before.UnitsAvailable);
            Assert.Equal(2, before.ExpiringSoonUnits);

            Assert.Equal(1, await service.ProcessExpiredPacketsAsync());
            var after = (await service.GetHospitalInventoryAsync(hospitalId)).Single();
            Assert.Equal(2, after.UnitsAvailable);
            Assert.Equal(1, after.ExpiringSoonUnits);
            Assert.Single(context.InventoryTransactions.Where(t => t.TransactionType == TransactionType.Expired));
        }

        [Fact]
        public async Task Packet_History_Shows_Every_Audited_Event()
        {
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = await AddHospitalAsync(context);
            await AddPacketsAsync(context, hospitalId, "B-", 1);
            var packet = await context.BloodPackets.SingleAsync();

            var result = (await service.GetPacketsAsync(null, null, null, packet.PacketId)).Single();

            Assert.StartsWith("PKT-", result.PacketCode);
            Assert.Equal(BloodPacket.StandardVolumeMl, result.VolumeMl);
            Assert.Single(result.History!);
        }

        [Fact]
        public async Task DeleteInventoryAsync_Removes_An_Unused_Category_But_Keeps_Categories_With_History()
        {
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = await AddHospitalAsync(context);
            var empty = await service.CreateInventoryAsync(new CreateInventoryDto { HospitalId = hospitalId, BloodGroup = "AB-", MinimumThreshold = 5, MaximumCapacity = 50 });
            await AddPacketsAsync(context, hospitalId, "O+", 1);
            var withHistory = await context.BloodInventories.SingleAsync(i => i.BloodGroup == "O+");

            Assert.True(await service.DeleteInventoryAsync(empty.InventoryId));
            Assert.Null(await context.BloodInventories.FindAsync(empty.InventoryId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteInventoryAsync(withHistory.InventoryId));
        }
    }
}
