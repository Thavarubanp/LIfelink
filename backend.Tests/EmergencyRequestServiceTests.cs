using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Emergency;
using LifeLink.DTOs.Inventory;
using LifeLink.Entities;
using LifeLink.Services.Emergency;
using LifeLink.Services.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeLink.Tests
{
    public class EmergencyRequestServiceTests
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
        public async Task CreateEmergencyRequestAsync_ValidInput_CreatesPendingRequest()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new EmergencyRequestService(context);
            var hospitalId = Guid.NewGuid();

            var dto = new EmergencyRequestCreateDto
            {
                HospitalId = hospitalId,
                BloodGroup = "O+",
                UnitsRequired = 10,
                Priority = "Critical",
                Reason = "Trauma patient emergency surgery"
            };

            // Act
            var result = await service.CreateEmergencyRequestAsync(dto);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Pending", result.Status);
            Assert.Equal("Critical", result.Priority);
            Assert.Equal(10, result.UnitsRequired);
        }

        [Fact]
        public async Task CreateEmergencyRequestAsync_ZeroUnits_ThrowsInvalidOperationException()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new EmergencyRequestService(context);

            var dto = new EmergencyRequestCreateDto
            {
                HospitalId = Guid.NewGuid(),
                BloodGroup = "A+",
                UnitsRequired = 0,
                Priority = "High",
                Reason = "Test"
            };

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateEmergencyRequestAsync(dto));
        }

        [Fact]
        public async Task ApproveEmergencyRequestAsync_PendingRequest_ChangesStatusToApproved()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new EmergencyRequestService(context);
            var created = await service.CreateEmergencyRequestAsync(new EmergencyRequestCreateDto
            {
                HospitalId = Guid.NewGuid(),
                BloodGroup = "B+",
                UnitsRequired = 5,
                Priority = "High",
                Reason = "Surgery request"
            });

            // Act
            var approved = await service.ApproveEmergencyRequestAsync(created.EmergencyRequestId);

            // Assert
            Assert.Equal("Approved", approved.Status);
        }

        [Fact]
        public async Task RejectEmergencyRequestAsync_PendingRequest_ChangesStatusToRejected()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new EmergencyRequestService(context);
            var created = await service.CreateEmergencyRequestAsync(new EmergencyRequestCreateDto
            {
                HospitalId = Guid.NewGuid(),
                BloodGroup = "AB-",
                UnitsRequired = 2,
                Priority = "Low",
                Reason = "Standard request"
            });

            // Act
            var rejected = await service.RejectEmergencyRequestAsync(created.EmergencyRequestId);

            // Assert
            Assert.Equal("Rejected", rejected.Status);
        }

        [Fact]
        public async Task CompleteEmergencyRequestAsync_RejectedRequest_ThrowsInvalidOperationException()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new EmergencyRequestService(context);
            var created = await service.CreateEmergencyRequestAsync(new EmergencyRequestCreateDto
            {
                HospitalId = Guid.NewGuid(),
                BloodGroup = "O-",
                UnitsRequired = 4,
                Priority = "Medium",
                Reason = "ICU requirement"
            });

            await service.RejectEmergencyRequestAsync(created.EmergencyRequestId);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteEmergencyRequestAsync(created.EmergencyRequestId));
            Assert.Contains("rejected", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CompleteEmergencyRequestAsync_ApprovedRequest_MarksCompleted_Without_Deducting_Stock_By_Count()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var emergencyService = new EmergencyRequestService(context);
            var inventoryService = new BloodInventoryService(context);
            var hospitalId = Guid.NewGuid();

            // 30 packets of stock (the ledger's seed path stands in for real packets)
            await context.Hospitals.AddAsync(new Hospital { HospitalId = hospitalId, Name = "Emergency Hospital", Email = "er@h.org", IsVerified = true });
            await context.SaveChangesAsync();
            await InventoryLedger.AddCollectedPacketsAsync(context, hospitalId, "A+", 30, DateTime.UtcNow,
                BloodPacketSource.Seed, null, TransactionType.Seeded, "test stock", null);
            await context.SaveChangesAsync();

            var created = await emergencyService.CreateEmergencyRequestAsync(new EmergencyRequestCreateDto
            {
                HospitalId = hospitalId,
                BloodGroup = "A+",
                UnitsRequired = 10,
                Priority = "Critical",
                Reason = "Accident victim"
            });

            await emergencyService.ApproveEmergencyRequestAsync(created.EmergencyRequestId);

            // Act
            var completed = await emergencyService.CompleteEmergencyRequestAsync(created.EmergencyRequestId);

            // Assert: stock used for an emergency is issued packet by packet from the inventory page, never by count
            Assert.Equal("Completed", completed.Status);
            var hospitalInventory = (await inventoryService.GetHospitalInventoryAsync(hospitalId)).First(i => i.BloodGroup == "A+");
            Assert.Equal(30, hospitalInventory.UnitsAvailable);
            Assert.Equal(0, await context.BloodPackets.CountAsync(p => p.Status == BloodPacketStatus.Issued));
        }

        [Fact]
        public async Task GetCriticalEmergencyRequestsAsync_ReturnsOnlyCriticalPriority()
        {
            // Arrange
            using var context = GetInMemoryDbContext();
            var service = new EmergencyRequestService(context);
            var hospitalId = Guid.NewGuid();

            await service.CreateEmergencyRequestAsync(new EmergencyRequestCreateDto
            {
                HospitalId = hospitalId,
                BloodGroup = "O+",
                UnitsRequired = 10,
                Priority = "Critical",
                Reason = "Urgent transfusions needed"
            });

            await service.CreateEmergencyRequestAsync(new EmergencyRequestCreateDto
            {
                HospitalId = hospitalId,
                BloodGroup = "B-",
                UnitsRequired = 2,
                Priority = "Low",
                Reason = "Routine replenishment"
            });

            // Act
            var criticalRequests = (await service.GetCriticalEmergencyRequestsAsync()).ToList();

            // Assert
            Assert.Single(criticalRequests);
            Assert.Equal("Critical", criticalRequests.First().Priority);
        }

        [Fact]
        public async Task Emergency_Alerts_Go_Only_To_Hospitals_Holding_The_Exact_Blood_Group()
        {
            var context = GetInMemoryDbContext();
            Hospital H(string name, bool suspended = false) => new() { HospitalId = Guid.NewGuid(), Name = name, Email = $"{Guid.NewGuid():N}@example.test", IsVerified = true, IsSuspended = suspended, ApprovalStatus = ApprovalStatus.Approved };
            var requester = H("Requester");
            var exact = H("Exact");
            var compatible = H("Compatible only");
            var suspended = H("Suspended", suspended: true);
            context.Hospitals.AddRange(requester, exact, compatible, suspended);
            BloodPacket P(Hospital h, string group, int days = 30) => new() { HospitalId = h.HospitalId, CreatedByHospitalId = h.HospitalId, BloodGroup = group,
                TrackingNumber = $"PKT-T{Guid.NewGuid():N}"[..12], CollectionDate = DateTime.UtcNow.AddDays(-1), ExpiryDate = DateTime.UtcNow.AddDays(days) };
            context.BloodPackets.AddRange(P(exact, "A+"), P(exact, "A+"), P(compatible, "O-"), P(suspended, "A+"), P(requester, "A+"), P(compatible, "A+", days: -1));
            await context.SaveChangesAsync();

            var holders = await new EmergencyRequestService(context).FindHospitalsWithExactGroupStockAsync(
                new EmergencyRequest { HospitalId = requester.HospitalId, BloodGroup = "A+", UnitsRequired = 2 });

            var holder = Assert.Single(holders); // O- can be given to A+, but only the exact group counts; expired packets do not
            Assert.Equal(exact.HospitalId, holder.HospitalId);
            Assert.Equal(2, holder.Units);
        }
    }
}
