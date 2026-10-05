using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Controllers;
using LifeLink.Data;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Inventory;
using LifeLink.Entities;
using LifeLink.Services.Common;
using LifeLink.Services.Inventory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeLink.Tests
{
    public class InventoryAuthorizationAndRoleAttentionTests
    {
        private sealed class FakeCurrentUser : ICurrentUserService
        {
            public Guid? UserId { get; init; }
            public string? Email { get; init; }
            public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
            public bool IsAuthenticated => UserId.HasValue;
        }

        private static AppDbContext NewContext()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();
            return context;
        }

        private static InventoryController InventoryControllerFor(AppDbContext context, FakeCurrentUser user) =>
            new(new BloodInventoryService(context), user, context);

        private static List<InventoryResponseDto> InventoryRows(IActionResult result) =>
            ((ApiResponse<IEnumerable<InventoryResponseDto>>)((OkObjectResult)result).Value!).Data!.ToList();

        private static async Task<(Hospital A, Hospital B, BloodInventory AInventory, BloodInventory BInventory)> SeedInventoryAsync(AppDbContext context)
        {
            var a = new Hospital { HospitalId = Guid.NewGuid(), Name = "Hospital A", Email = "a@hospital.test", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved };
            var b = new Hospital { HospitalId = Guid.NewGuid(), Name = "Hospital B", Email = "b@hospital.test", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved };
            var ai = new BloodInventory { InventoryId = Guid.NewGuid(), HospitalId = a.HospitalId, BloodGroup = "A+", UnitsAvailable = 1, MinimumThreshold = 3, MaximumCapacity = 10 };
            var bi = new BloodInventory { InventoryId = Guid.NewGuid(), HospitalId = b.HospitalId, BloodGroup = "O-", UnitsAvailable = 9, MinimumThreshold = 1, MaximumCapacity = 10 };
            context.AddRange(a, b, ai, bi);
            context.InventoryTransactions.Add(new InventoryTransaction
            {
                TransactionId = Guid.NewGuid(), InventoryId = bi.InventoryId,
                TransactionType = TransactionType.StockAddition, Units = 9, Notes = "Seed", CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            return (a, b, ai, bi);
        }

        [Fact]
        public async Task HospitalStaff_Inventory_Reads_Are_Restricted_To_Own_Hospital()
        {
            using var context = NewContext();
            var seeded = await SeedInventoryAsync(context);
            var user = new FakeCurrentUser { UserId = Guid.NewGuid(), Email = seeded.A.Email, Roles = new[] { "HospitalStaff" } };
            var controller = InventoryControllerFor(context, user);

            Assert.Single(InventoryRows(await controller.GetHospitalInventory(seeded.A.HospitalId)));
            Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.GetHospitalInventory(seeded.B.HospitalId)).StatusCode);
            Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.GetInventoryById(seeded.BInventory.InventoryId)).StatusCode);
            Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.GetInventoryTransactions(seeded.BInventory.InventoryId)).StatusCode);

            var all = InventoryRows(await controller.GetAllInventory());
            Assert.Single(all);
            Assert.All(all, row => Assert.Equal(seeded.A.HospitalId, row.HospitalId));
            var low = InventoryRows(await controller.GetLowStockInventory());
            Assert.Single(low);
            Assert.Equal(seeded.A.HospitalId, low[0].HospitalId);
            Assert.Empty(InventoryRows(await controller.GetSurplusInventory()));
        }

        [Theory]
        [InlineData("Admin")]
        [InlineData("InternalAgent")]
        public async Task Trusted_Inventory_Readers_Retain_Cross_Hospital_Access(string role)
        {
            using var context = NewContext();
            var seeded = await SeedInventoryAsync(context);
            var controller = InventoryControllerFor(context,
                new FakeCurrentUser { UserId = Guid.NewGuid(), Roles = new[] { role } });

            Assert.Equal(2, InventoryRows(await controller.GetAllInventory()).Count);
            Assert.IsType<OkObjectResult>(await controller.GetHospitalInventory(seeded.B.HospitalId));
            Assert.IsType<OkObjectResult>(await controller.GetInventoryById(seeded.BInventory.InventoryId));
            Assert.IsType<OkObjectResult>(await controller.GetInventoryTransactions(seeded.BInventory.InventoryId));
            Assert.Single(InventoryRows(await controller.GetLowStockInventory()));
            Assert.Single(InventoryRows(await controller.GetSurplusInventory()));
        }

        [Fact]
        public async Task Hospital_Attention_Counts_Only_Own_Pending_Verifications_And_Incoming_Responses()
        {
            using var context = NewContext();
            var seeded = await SeedInventoryAsync(context);
            var staff = new FakeCurrentUser { UserId = Guid.NewGuid(), Email = seeded.A.Email, Roles = new[] { "HospitalStaff" } };
            var patient = Guid.NewGuid();
            context.BloodRequests.AddRange(
                new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = patient, HospitalId = seeded.A.HospitalId, BloodGroup = "A+", UnitsRequired = 1, Reason = "A", Priority = "Normal", Status = BloodRequestStatus.Pending },
                new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = patient, HospitalId = seeded.A.HospitalId, BloodGroup = "B+", UnitsRequired = 1, Reason = "B", Priority = "Normal", Status = BloodRequestStatus.Verified },
                new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = patient, HospitalId = seeded.B.HospitalId, BloodGroup = "O-", UnitsRequired = 1, Reason = "B", Priority = "Normal", Status = BloodRequestStatus.Pending });
            context.HospitalTransferRequests.AddRange(
                // A initiated this Request, so B is the counterpart. It must not count for A.
                new HospitalTransferRequest { TransferRequestId = Guid.NewGuid(), SenderHospitalId = seeded.B.HospitalId, ReceiverHospitalId = seeded.A.HospitalId, TransferType = TransferTypes.Request, BloodGroup = "O-", UnitsRequested = 1, Status = "Pending" },
                // B initiated this Request; A is its sender/counterpart and must respond.
                new HospitalTransferRequest { TransferRequestId = Guid.NewGuid(), SenderHospitalId = seeded.A.HospitalId, ReceiverHospitalId = seeded.B.HospitalId, TransferType = TransferTypes.Request, BloodGroup = "A+", UnitsRequested = 1, Status = "Pending" },
                // B offered to A; A is the receiver/counterpart and must respond.
                new HospitalTransferRequest { TransferRequestId = Guid.NewGuid(), SenderHospitalId = seeded.B.HospitalId, ReceiverHospitalId = seeded.A.HospitalId, TransferType = TransferTypes.Offer, BloodGroup = "O-", UnitsRequested = 1, Status = "Pending" },
                new HospitalTransferRequest { TransferRequestId = Guid.NewGuid(), SenderHospitalId = seeded.A.HospitalId, ReceiverHospitalId = seeded.B.HospitalId, TransferType = TransferTypes.Request, BloodGroup = "A+", UnitsRequested = 1, Status = "Completed" });
            await context.SaveChangesAsync();

            var service = new RoleAttentionService(context, staff);
            var first = await service.GetAsync();
            Assert.Equal(1, first.PendingHospitalVerifications);
            Assert.Equal(2, first.PendingTransferResponses);

            var pending = await context.BloodRequests.SingleAsync(r => r.HospitalId == seeded.A.HospitalId && r.Status == BloodRequestStatus.Pending);
            pending.Status = BloodRequestStatus.Verified;
            var incoming = await context.HospitalTransferRequests.FirstAsync(t => t.TransferType == TransferTypes.Offer && t.ReceiverHospitalId == seeded.A.HospitalId);
            incoming.Status = TransferRequestStatus.Rejected.ToString();
            await context.SaveChangesAsync();

            var after = await service.GetAsync();
            Assert.Equal(0, after.PendingHospitalVerifications);
            Assert.Equal(1, after.PendingTransferResponses);
        }

        [Fact]
        public async Task Doctor_Attention_Uses_Latest_Pending_ScreeningCompleted_Report_In_Own_Hospital()
        {
            using var context = NewContext();
            var seeded = await SeedInventoryAsync(context);
            var doctorUser = new User { UserId = Guid.NewGuid(), Email = "doctor@a.test", FirstName = "A", LastName = "Doctor" };
            var doctor = new Doctor { DoctorId = Guid.NewGuid(), UserId = doctorUser.UserId, HospitalId = seeded.A.HospitalId, Email = doctorUser.Email, FirstName = "A", LastName = "Doctor", IsActive = true };
            var requestA = new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = Guid.NewGuid(), HospitalId = seeded.A.HospitalId, BloodGroup = "A+", UnitsRequired = 1, Reason = "A", Priority = "Normal", Status = BloodRequestStatus.Approved };
            var requestB = new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = Guid.NewGuid(), HospitalId = seeded.B.HospitalId, BloodGroup = "O-", UnitsRequired = 1, Reason = "B", Priority = "Normal", Status = BloodRequestStatus.Approved };
            var acceptanceA = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = requestA.BloodRequestId, DonorUserId = Guid.NewGuid(), Status = AcceptanceStatus.ScreeningCompleted };
            var acceptanceNotReady = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = requestA.BloodRequestId, DonorUserId = Guid.NewGuid(), Status = AcceptanceStatus.ScreeningPending };
            var acceptanceB = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = requestB.BloodRequestId, DonorUserId = Guid.NewGuid(), Status = AcceptanceStatus.ScreeningCompleted };
            context.AddRange(doctorUser, doctor, requestA, requestB, acceptanceA, acceptanceNotReady, acceptanceB);
            context.DonorVerifications.AddRange(
                new DonorVerification { DonorVerificationId = Guid.NewGuid(), AcceptanceId = acceptanceA.AcceptanceId, ReportVersion = 1, Status = VerificationStatus.Pending },
                new DonorVerification { DonorVerificationId = Guid.NewGuid(), AcceptanceId = acceptanceA.AcceptanceId, ReportVersion = 2, Status = VerificationStatus.Pending },
                new DonorVerification { DonorVerificationId = Guid.NewGuid(), AcceptanceId = acceptanceNotReady.AcceptanceId, ReportVersion = 1, Status = VerificationStatus.Pending },
                new DonorVerification { DonorVerificationId = Guid.NewGuid(), AcceptanceId = acceptanceB.AcceptanceId, ReportVersion = 1, Status = VerificationStatus.Pending });
            await context.SaveChangesAsync();

            var result = await new RoleAttentionService(context,
                new FakeCurrentUser { UserId = doctorUser.UserId, Email = doctorUser.Email, Roles = new[] { "Doctor" } }).GetAsync();

            Assert.Equal(1, result.PendingScreeningReviews);
            Assert.Equal(0, result.PendingHospitalVerifications);
            Assert.Equal(0, result.PendingTransferResponses);
        }

        [Fact]
        public async Task Unsupported_Roles_Receive_No_Privileged_Attention_Counts()
        {
            using var context = NewContext();
            await SeedInventoryAsync(context);
            var result = await new RoleAttentionService(context,
                new FakeCurrentUser { UserId = Guid.NewGuid(), Roles = new[] { "User" } }).GetAsync();

            Assert.Equal(0, result.PendingHospitalVerifications);
            Assert.Equal(0, result.PendingTransferResponses);
            Assert.Equal(0, result.PendingScreeningReviews);
        }
    }
}
