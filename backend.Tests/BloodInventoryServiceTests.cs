using System;
using System.Collections.Generic;
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

        [Fact]
        public async Task CreateInventoryAsync_ValidInput_CreatesInventoryAndTransaction()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = Guid.NewGuid();

            var dto = new CreateInventoryDto
            {
                HospitalId = hospitalId,
                BloodGroup = "A+",
                UnitsAvailable = 50,
                MinimumThreshold = 10,
                MaximumCapacity = 100
            };

            // Act
            var result = await service.CreateInventoryAsync(dto);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(hospitalId, result.HospitalId);
            Assert.Equal("A+", result.BloodGroup);
            Assert.Equal(50, result.UnitsAvailable);

            var transactions = await service.GetInventoryTransactionsAsync(result.InventoryId);
            Assert.Single(transactions);
            Assert.Equal(TransactionType.InitialStock, transactions.First().TransactionType);
        }

        [Fact]
        public async Task CreateInventoryAsync_NegativeUnits_ThrowsInvalidOperationException()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);

            var dto = new CreateInventoryDto
            {
                HospitalId = Guid.NewGuid(),
                BloodGroup = "O+",
                UnitsAvailable = -5,
                MinimumThreshold = 10,
                MaximumCapacity = 100
            };

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInventoryAsync(dto));
        }

        [Fact]
        public async Task CreateInventoryAsync_UnitsExceedCapacity_ThrowsInvalidOperationException()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);

            var dto = new CreateInventoryDto
            {
                HospitalId = Guid.NewGuid(),
                BloodGroup = "B+",
                UnitsAvailable = 150,
                MinimumThreshold = 10,
                MaximumCapacity = 100
            };

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInventoryAsync(dto));
        }

        [Fact]
        public async Task CreateInventoryAsync_InvalidBloodGroup_ThrowsInvalidOperationException()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);

            var dto = new CreateInventoryDto
            {
                HospitalId = Guid.NewGuid(),
                BloodGroup = "INVALID",
                UnitsAvailable = 20,
                MinimumThreshold = 5,
                MaximumCapacity = 50
            };

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInventoryAsync(dto));
        }

        [Fact]
        public async Task CreateInventoryAsync_DuplicateHospitalAndBloodGroup_ThrowsInvalidOperationException()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = Guid.NewGuid();

            var dto = new CreateInventoryDto
            {
                HospitalId = hospitalId,
                BloodGroup = "AB+",
                UnitsAvailable = 20,
                MinimumThreshold = 5,
                MaximumCapacity = 50
            };

            await service.CreateInventoryAsync(dto);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInventoryAsync(dto));
        }

        [Fact]
        public async Task UpdateInventoryAsync_ValidInput_UpdatesStockAndAddsTransaction()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var created = await service.CreateInventoryAsync(new CreateInventoryDto
            {
                HospitalId = Guid.NewGuid(),
                BloodGroup = "O-",
                UnitsAvailable = 20,
                MinimumThreshold = 10,
                MaximumCapacity = 100
            });

            var updateDto = new UpdateInventoryDto
            {
                UnitsAvailable = 35,
                MinimumThreshold = 10,
                MaximumCapacity = 100,
                AuditNotes = "Restocked 15 units"
            };

            // Act
            var updated = await service.UpdateInventoryAsync(created.InventoryId, updateDto);

            // Assert
            Assert.Equal(35, updated.UnitsAvailable);
            var transactions = (await service.GetInventoryTransactionsAsync(created.InventoryId)).ToList();
            Assert.Equal(2, transactions.Count);
            Assert.Equal(TransactionType.StockAddition, transactions.First().TransactionType);
            Assert.Equal(15, transactions.First().Units);
        }

        [Fact]
        public async Task GetLowStockInventoryAsync_ReturnsOnlyLowStock()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = Guid.NewGuid();

            await service.CreateInventoryAsync(new CreateInventoryDto
            {
                HospitalId = hospitalId,
                BloodGroup = "A+",
                UnitsAvailable = 5,
                MinimumThreshold = 10,
                MaximumCapacity = 100
            });

            await service.CreateInventoryAsync(new CreateInventoryDto
            {
                HospitalId = hospitalId,
                BloodGroup = "B+",
                UnitsAvailable = 50,
                MinimumThreshold = 10,
                MaximumCapacity = 100
            });

            // Act
            var lowStock = (await service.GetLowStockInventoryAsync()).ToList();

            // Assert
            Assert.Single(lowStock);
            Assert.Equal("A+", lowStock.First().BloodGroup);
        }

        [Fact]
        public async Task GetSurplusInventoryAsync_ReturnsOnlySurplusStock()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var hospitalId = Guid.NewGuid();

            await service.CreateInventoryAsync(new CreateInventoryDto
            {
                HospitalId = hospitalId,
                BloodGroup = "O+",
                UnitsAvailable = 90,
                MinimumThreshold = 10,
                MaximumCapacity = 100
            });

            await service.CreateInventoryAsync(new CreateInventoryDto
            {
                HospitalId = hospitalId,
                BloodGroup = "A-",
                UnitsAvailable = 30,
                MinimumThreshold = 10,
                MaximumCapacity = 100
            });

            // Act
            var surplus = (await service.GetSurplusInventoryAsync()).ToList();

            // Assert
            Assert.Single(surplus);
            Assert.Equal("O+", surplus.First().BloodGroup);
        }

        [Fact]
        public async Task DeleteInventoryAsync_ExistingInventory_RemovesRecord()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new BloodInventoryService(context);
            var created = await service.CreateInventoryAsync(new CreateInventoryDto
            {
                HospitalId = Guid.NewGuid(),
                BloodGroup = "AB-",
                UnitsAvailable = 10,
                MinimumThreshold = 5,
                MaximumCapacity = 50
            });

            // Act
            var result = await service.DeleteInventoryAsync(created.InventoryId);

            // Assert
            Assert.True(result);
            var fetched = await service.GetInventoryByIdAsync(created.InventoryId);
            Assert.Null(fetched);
        }
    }
}
