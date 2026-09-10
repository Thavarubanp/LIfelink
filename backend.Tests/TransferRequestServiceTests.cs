using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Inventory;
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
        public async Task CreateTransferRequestAsync_SameSenderAndReceiver_ThrowsInvalidOperationException()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new TransferRequestService(context);
            var hospitalId = Guid.NewGuid();

            var dto = new TransferRequestCreateDto
            {
                SenderHospitalId = hospitalId,
                ReceiverHospitalId = hospitalId,
                BloodGroup = "A+",
                UnitsRequested = 10,
                Notes = "Invalid self-transfer"
            };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateTransferRequestAsync(dto));
            Assert.Contains("same", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CreateTransferRequestAsync_ZeroUnits_ThrowsInvalidOperationException()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new TransferRequestService(context);

            var dto = new TransferRequestCreateDto
            {
                SenderHospitalId = Guid.NewGuid(),
                ReceiverHospitalId = Guid.NewGuid(),
                BloodGroup = "O-",
                UnitsRequested = 0,
                Notes = "Zero units"
            };

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateTransferRequestAsync(dto));
        }

        [Fact]
        public async Task CreateTransferRequestAsync_ValidInput_CreatesPendingTransfer()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new TransferRequestService(context);
            var senderId = Guid.NewGuid();
            var receiverId = Guid.NewGuid();

            var dto = new TransferRequestCreateDto
            {
                SenderHospitalId = senderId,
                ReceiverHospitalId = receiverId,
                BloodGroup = "B+",
                UnitsRequested = 15,
                Notes = "Urgent replenishment for Receiver"
            };

            // Act
            var result = await service.CreateTransferRequestAsync(dto);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Pending", result.Status);
            Assert.Equal(senderId, result.SenderHospitalId);
            Assert.Equal(receiverId, result.ReceiverHospitalId);
            Assert.Equal(15, result.UnitsRequested);
        }

        [Fact]
        public async Task RejectTransferRequestAsync_ApprovedTransfer_ThrowsInvalidOperationException()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new TransferRequestService(context);
            var created = await service.CreateTransferRequestAsync(new TransferRequestCreateDto
            {
                SenderHospitalId = Guid.NewGuid(),
                ReceiverHospitalId = Guid.NewGuid(),
                BloodGroup = "AB+",
                UnitsRequested = 5,
                Notes = "Normal request"
            });

            await service.ApproveTransferRequestAsync(created.TransferRequestId);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RejectTransferRequestAsync(created.TransferRequestId));
            Assert.Contains("Approved transfer requests cannot be rejected", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CompleteTransferRequestAsync_ApprovedTransfer_TransfersStockBetweenHospitals()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var transferService = new TransferRequestService(context);
            var inventoryService = new BloodInventoryService(context);

            var senderHospitalId = Guid.NewGuid();
            var receiverHospitalId = Guid.NewGuid();

            // Sender has 50 units of O+
            await inventoryService.CreateInventoryAsync(new CreateInventoryDto
            {
                HospitalId = senderHospitalId,
                BloodGroup = "O+",
                UnitsAvailable = 50,
                MinimumThreshold = 10,
                MaximumCapacity = 100
            });

            // Receiver has 5 units of O+
            await inventoryService.CreateInventoryAsync(new CreateInventoryDto
            {
                HospitalId = receiverHospitalId,
                BloodGroup = "O+",
                UnitsAvailable = 5,
                MinimumThreshold = 10,
                MaximumCapacity = 100
            });

            var created = await transferService.CreateTransferRequestAsync(new TransferRequestCreateDto
            {
                SenderHospitalId = senderHospitalId,
                ReceiverHospitalId = receiverHospitalId,
                BloodGroup = "O+",
                UnitsRequested = 20,
                Notes = "Stock rebalance"
            });

            await transferService.ApproveTransferRequestAsync(created.TransferRequestId);

            // Act
            var completed = await transferService.CompleteTransferRequestAsync(created.TransferRequestId);

            // Assert
            Assert.Equal("Completed", completed.Status);

            var senderInv = (await inventoryService.GetHospitalInventoryAsync(senderHospitalId)).First(i => i.BloodGroup == "O+");
            var receiverInv = (await inventoryService.GetHospitalInventoryAsync(receiverHospitalId)).First(i => i.BloodGroup == "O+");

            Assert.Equal(30, senderInv.UnitsAvailable); // 50 - 20
            Assert.Equal(25, receiverInv.UnitsAvailable); // 5 + 20
        }

        [Fact]
        public async Task CompleteTransferRequestAsync_AlreadyCompleted_ThrowsInvalidOperationException()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new TransferRequestService(context);
            var created = await service.CreateTransferRequestAsync(new TransferRequestCreateDto
            {
                SenderHospitalId = Guid.NewGuid(),
                ReceiverHospitalId = Guid.NewGuid(),
                BloodGroup = "A-",
                UnitsRequested = 3,
                Notes = "Inter-hospital transfer"
            });

            await service.ApproveTransferRequestAsync(created.TransferRequestId);
            await service.CompleteTransferRequestAsync(created.TransferRequestId);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteTransferRequestAsync(created.TransferRequestId));
            Assert.Contains("Completed transfer requests cannot be modified", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetPendingTransferRequestsAsync_ReturnsOnlyPendingTransfers()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new TransferRequestService(context);

            var pending = await service.CreateTransferRequestAsync(new TransferRequestCreateDto
            {
                SenderHospitalId = Guid.NewGuid(),
                ReceiverHospitalId = Guid.NewGuid(),
                BloodGroup = "O+",
                UnitsRequested = 10,
                Notes = "Pending transfer"
            });

            var approved = await service.CreateTransferRequestAsync(new TransferRequestCreateDto
            {
                SenderHospitalId = Guid.NewGuid(),
                ReceiverHospitalId = Guid.NewGuid(),
                BloodGroup = "B-",
                UnitsRequested = 4,
                Notes = "To be approved"
            });
            await service.ApproveTransferRequestAsync(approved.TransferRequestId);

            // Act
            var pendingList = (await service.GetPendingTransferRequestsAsync()).ToList();

            // Assert
            Assert.Single(pendingList);
            Assert.Equal(pending.TransferRequestId, pendingList.First().TransferRequestId);
        }
    }
}
