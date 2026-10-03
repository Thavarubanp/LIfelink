using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.DTOs.BloodRequests;
using LifeLink.DTOs.Inventory;
using LifeLink.DTOs.Planning;
using LifeLink.DTOs.Transfer;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using LifeLink.Services.BloodCompatibility;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Inventory;
using LifeLink.Services.Planning;
using LifeLink.Services.Transfer;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Hospital blood packets (R1-R4), hospital "Donate Blood" with doctor approval (R5) and doctor choice on hospital
    /// blood requests (R6).
    /// </summary>
    public class HospitalPacketsAndDonationsTests
    {
        private const int HospitalStaffRoleId = 2; // seeded by AppDbContext.HasData

        private sealed class Seed
        {
            public string DbName = null!;
            public AppDbContext Context = null!;
            public Hospital A = null!;
            public Hospital B = null!;
            public User StaffA = null!;
            public User StaffB = null!;
            public User Patient = null!;
            public Doctor DoctorA = null!;       // assigned doctor at A
            public Doctor OtherDoctorA = null!;  // another doctor at A
            public Doctor DoctorB = null!;
            public Mock<IPlanningAgentService> Planning = null!;

            public AppDbContext NewContext() =>
                new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(DbName).Options);

            public BloodInventoryService Inventory => new(Context);
            public AcceptanceService Acceptances => new(Context, new BloodCompatibilityService(), Planning.Object);
        }

        private static async Task<Seed> SeedAsync()
        {
            var name = Guid.NewGuid().ToString();
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);
            context.Database.EnsureCreated();

            var a = new Hospital { HospitalId = Guid.NewGuid(), Name = "Hospital A", Email = "a@h.org", IsVerified = true, PacketShelfLifeDays = 35 };
            var b = new Hospital { HospitalId = Guid.NewGuid(), Name = "Hospital B", Email = "b@h.org", IsVerified = true, PacketShelfLifeDays = 35 };
            var staffA = new User { UserId = Guid.NewGuid(), FirstName = "Hospital A", LastName = "Staff", Email = "a@h.org" };
            var staffB = new User { UserId = Guid.NewGuid(), FirstName = "Hospital B", LastName = "Staff", Email = "b@h.org" };
            var patient = new User { UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Ient", Email = "patient@h.org" };
            var doctorA = new Doctor { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = a.HospitalId, FirstName = "Ann", LastName = "Doc", Email = "ann@a.org", IsActive = true, MustChangePassword = false };
            var otherDoctorA = new Doctor { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = a.HospitalId, FirstName = "Ola", LastName = "Doc", Email = "ola@a.org", IsActive = true, MustChangePassword = false };
            var doctorB = new Doctor { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = b.HospitalId, FirstName = "Ben", LastName = "Doc", Email = "ben@b.org", IsActive = true, MustChangePassword = false };

            await context.Hospitals.AddRangeAsync(a, b);
            await context.Users.AddRangeAsync(staffA, staffB, patient);
            await context.UserRoles.AddRangeAsync(
                new UserRole { UserId = staffA.UserId, RoleId = HospitalStaffRoleId },
                new UserRole { UserId = staffB.UserId, RoleId = HospitalStaffRoleId });
            await context.Doctors.AddRangeAsync(doctorA, otherDoctorA, doctorB);
            await context.SaveChangesAsync();

            return new Seed
            {
                DbName = name, Context = context, A = a, B = b, StaffA = staffA, StaffB = staffB, Patient = patient,
                DoctorA = doctorA, OtherDoctorA = otherDoctorA, DoctorB = doctorB, Planning = new Mock<IPlanningAgentService>()
            };
        }

        private static DateOnly Today => PacketDateRules.Today(DateTime.UtcNow);

        private static async Task<List<BloodPacketResponseDto>> CreatePacketsAsync(Seed s, Guid hospitalId, string group, int quantity, DateOnly? collected = null) =>
            await s.Inventory.CreatePacketsAsync(hospitalId, new CreateBloodPacketsDto { BloodGroup = group, CollectionDate = collected ?? Today, Quantity = quantity }, null);

        private static async Task<int> UnitsAsync(AppDbContext context, Guid hospitalId, string group) =>
            (await context.BloodInventories.SingleOrDefaultAsync(i => i.HospitalId == hospitalId && i.BloodGroup == group))?.UnitsAvailable ?? 0;

        /// <summary>An Approved (public) request with its assigned doctor, as after hospital verification and doctor approval.</summary>
        private static async Task<BloodRequest> AddApprovedRequestAsync(Seed s, Guid creatorId, Guid hospitalId, Guid doctorId, string group = "A+", int units = 2)
        {
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(), PatientUserId = creatorId, HospitalId = hospitalId, BloodGroup = group, UnitsRequired = units,
                Reason = "Surgery", Priority = "High", Status = BloodRequestStatus.Approved, ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await s.Context.BloodRequests.AddAsync(request);
            await s.Context.BloodRequestVerifications.AddAsync(new BloodRequestVerification
            {
                BloodRequestId = request.BloodRequestId, DoctorId = doctorId, Status = VerificationStatus.Approved, VerifiedAt = DateTime.UtcNow
            });
            await s.Context.SaveChangesAsync();
            return request;
        }

        // ---------------- R1 / R2: packets and collected date ----------------

        [Fact]
        public async Task Collected_Date_Is_Required_Cannot_Be_In_The_Future_And_Today_Or_Past_Is_Accepted()
        {
            var s = await SeedAsync();

            var missing = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                s.Inventory.CreatePacketsAsync(s.A.HospitalId, new CreateBloodPacketsDto { BloodGroup = "O+", CollectionDate = null }, null));
            Assert.Equal("Collected date is required.", missing.Message);

            var future = await Assert.ThrowsAsync<InvalidOperationException>(() => CreatePacketsAsync(s, s.A.HospitalId, "O+", 1, Today.AddDays(1)));
            Assert.Equal("Collected date cannot be in the future.", future.Message);

            var tooOld = await Assert.ThrowsAsync<InvalidOperationException>(() => CreatePacketsAsync(s, s.A.HospitalId, "O+", 1, Today.AddDays(-35)));
            Assert.Contains("would already be expired", tooOld.Message);

            Assert.Equal(0, await s.Context.BloodPackets.CountAsync());

            var today = Assert.Single(await CreatePacketsAsync(s, s.A.HospitalId, "O+", 1, Today));
            Assert.Equal(Today, DateOnly.FromDateTime(today.CollectionDate));
            var past = Assert.Single(await CreatePacketsAsync(s, s.A.HospitalId, "O+", 1, Today.AddDays(-10)));
            Assert.Equal(Today.AddDays(-10).AddDays(35), DateOnly.FromDateTime(past.ExpiryDate));
            Assert.Equal(2, await UnitsAsync(s.Context, s.A.HospitalId, "O+"));
        }

        [Fact]
        public void Today_Is_The_Sri_Lanka_Date()
        {
            // 19:00 UTC on 27 Sep is 00:30 on 28 Sep in Sri Lanka
            var utcNow = new DateTime(2026, 9, 27, 19, 0, 0, DateTimeKind.Utc);

            var stored = PacketDateRules.Validate(new DateOnly(2026, 9, 28), 35, utcNow);
            Assert.Equal(new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc), stored);
            Assert.Throws<InvalidOperationException>(() => PacketDateRules.Validate(new DateOnly(2026, 9, 29), 35, utcNow));
        }

        [Fact]
        public async Task Every_Packet_Gets_A_Unique_System_Tracking_Number_And_The_Creating_Hospital()
        {
            var s = await SeedAsync();
            var first = await CreatePacketsAsync(s, s.A.HospitalId, "O+", 20);
            var second = await CreatePacketsAsync(s, s.B.HospitalId, "O+", 20);
            var all = first.Concat(second).ToList();

            Assert.Equal(40, all.Select(p => p.TrackingNumber).Distinct().Count());
            Assert.All(all, p => Assert.Matches(new Regex("^PKT-\\d{8}$"), p.TrackingNumber));
            Assert.All(first, p => Assert.Equal(s.A.HospitalId, p.CreatedByHospitalId));
            Assert.All(all, p => Assert.Equal(BloodPacketSource.Manual, p.Source));
            Assert.All(all, p => Assert.NotEqual(default, p.CreatedAt));
            Assert.Equal(20, await UnitsAsync(s.Context, s.A.HospitalId, "O+"));

            // The database enforces uniqueness too
            var index = s.Context.Model.FindEntityType(typeof(BloodPacket))!.GetIndexes()
                .Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(BloodPacket.TrackingNumber));
            Assert.True(index.IsUnique);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                s.Inventory.CreatePacketsAsync(s.A.HospitalId, new CreateBloodPacketsDto { BloodGroup = "O+", CollectionDate = Today, Quantity = 21 }, null));
        }

        [Theory]
        [InlineData(nameof(BloodPacket.TrackingNumber))]
        [InlineData(nameof(BloodPacket.CreatedByHospitalId))]
        [InlineData(nameof(BloodPacket.CreatedAt))]
        public async Task Tracking_Number_Creator_And_Created_Date_Can_Never_Change(string property)
        {
            var s = await SeedAsync();
            var created = Assert.Single(await CreatePacketsAsync(s, s.A.HospitalId, "O+", 1));

            using var context = s.NewContext();
            var packet = await context.BloodPackets.FindAsync(created.PacketId);
            switch (property)
            {
                case nameof(BloodPacket.TrackingNumber): packet!.TrackingNumber = "PKT-99999999"; break;
                case nameof(BloodPacket.CreatedByHospitalId): packet!.CreatedByHospitalId = s.B.HospitalId; break;
                default: packet!.CreatedAt = DateTime.UtcNow.AddDays(-3); break;
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        // ---------------- R3: edit permission ----------------

        [Fact]
        public async Task Only_The_Creating_Hospital_Can_Edit_Its_Available_Packet()
        {
            var s = await SeedAsync();
            var packet = Assert.Single(await CreatePacketsAsync(s, s.A.HospitalId, "O+", 1));
            Assert.True(packet.CanEdit);

            var other = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                s.Inventory.UpdatePacketAsync(packet.PacketId, s.B.HospitalId, new UpdateBloodPacketDto { BloodGroup = "A+", CollectionDate = Today }, null));
            Assert.Contains("Only the hospital that created this packet", other.Message);

            var edited = await s.Inventory.UpdatePacketAsync(packet.PacketId, s.A.HospitalId,
                new UpdateBloodPacketDto { BloodGroup = "A+", CollectionDate = Today.AddDays(-5) }, null);

            Assert.Equal("A+", edited.BloodGroup);
            Assert.Equal(packet.TrackingNumber, edited.TrackingNumber);
            Assert.Equal(Today.AddDays(30), DateOnly.FromDateTime(edited.ExpiryDate));
            Assert.Equal(0, await UnitsAsync(s.Context, s.A.HospitalId, "O+"));
            Assert.Equal(1, await UnitsAsync(s.Context, s.A.HospitalId, "A+"));
            Assert.Single(s.Context.InventoryTransactions.Where(t => t.PacketId == packet.PacketId && t.TransactionType == TransactionType.Adjustment));

            // Future dates are refused on edit as well
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Inventory.UpdatePacketAsync(packet.PacketId, s.A.HospitalId,
                new UpdateBloodPacketDto { BloodGroup = "A+", CollectionDate = Today.AddDays(1) }, null));

            // Issued packets can no longer be edited
            var inventory = await s.Context.BloodInventories.SingleAsync(i => i.HospitalId == s.A.HospitalId && i.BloodGroup == "A+");
            await s.Inventory.UpdateInventoryAsync(inventory.InventoryId, new UpdateInventoryDto
            {
                MinimumThreshold = 0, MaximumCapacity = 100, AuditNotes = "Theatre", IssuePacketIds = new() { packet.PacketId }
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Inventory.UpdatePacketAsync(packet.PacketId, s.A.HospitalId,
                new UpdateBloodPacketDto { BloodGroup = "A+", CollectionDate = Today }, null));
        }

        [Fact]
        public async Task A_Transferred_Packet_Cannot_Be_Edited_By_Anyone()
        {
            var s = await SeedAsync();
            var packet = Assert.Single(await CreatePacketsAsync(s, s.A.HospitalId, "O+", 1));
            var transfers = new TransferRequestService(s.Context);
            var request = await transfers.CreateTransferRequestAsync(new TransferRequestCreateDto
            {
                TransferType = "Request", CounterpartHospitalId = s.A.HospitalId, BloodGroup = "O+", UnitsRequested = 1
            }, s.B.HospitalId);
            await transfers.ApproveTransferRequestAsync(request.TransferRequestId, s.A.HospitalId, null, new[] { packet.PacketId });

            var edit = new UpdateBloodPacketDto { BloodGroup = "O-", CollectionDate = Today };
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Inventory.UpdatePacketAsync(packet.PacketId, s.A.HospitalId, edit, null));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Inventory.UpdatePacketAsync(packet.PacketId, s.B.HospitalId, edit, null));

            var atReceiver = Assert.Single(await s.Inventory.GetPacketsAsync(s.B.HospitalId, null, null, null, s.B.HospitalId));
            Assert.False(atReceiver.CanEdit);
            Assert.Equal(s.A.HospitalId, atReceiver.CreatedByHospitalId);
            Assert.Empty(await s.Inventory.GetPacketsAsync(s.A.HospitalId, null, null, null, s.A.HospitalId));
        }

        // ---------------- R4: issuing by packet, double use ----------------

        [Fact]
        public async Task Issuing_Accepts_Only_Own_Available_Packets_Of_The_Group_And_Keeps_Them_As_Issued()
        {
            var s = await SeedAsync();
            var own = await CreatePacketsAsync(s, s.A.HospitalId, "O+", 3);
            var wrongGroup = Assert.Single(await CreatePacketsAsync(s, s.A.HospitalId, "B+", 1));
            var foreign = Assert.Single(await CreatePacketsAsync(s, s.B.HospitalId, "O+", 1));
            var inventory = await s.Context.BloodInventories.SingleAsync(i => i.HospitalId == s.A.HospitalId && i.BloodGroup == "O+");

            Task Issue(params Guid[] ids) => s.Inventory.UpdateInventoryAsync(inventory.InventoryId, new UpdateInventoryDto
            {
                MinimumThreshold = 0, MaximumCapacity = 100, AuditNotes = "Ward 3", IssuePacketIds = ids.ToList()
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => Issue(wrongGroup.PacketId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Issue(foreign.PacketId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Issue(own[0].PacketId, own[0].PacketId));
            Assert.Equal(3, await UnitsAsync(s.Context, s.A.HospitalId, "O+"));

            await Issue(own[0].PacketId, own[1].PacketId);
            Assert.Equal(1, await UnitsAsync(s.Context, s.A.HospitalId, "O+"));

            // Issued packets are kept (not deleted) and listed in the hospital's packet history
            var issued = (await s.Inventory.GetPacketsAsync(s.A.HospitalId, null, BloodPacketStatus.Issued, null, s.A.HospitalId)).ToList();
            Assert.Equal(2, issued.Count);
            Assert.DoesNotContain(await s.Inventory.GetPacketsAsync(s.A.HospitalId, null, BloodPacketStatus.Available, null), p => issued.Any(i => i.PacketId == p.PacketId));

            // Already issued: refused
            var again = await Assert.ThrowsAsync<InvalidOperationException>(() => Issue(own[0].PacketId));
            Assert.Contains("Issued", again.Message);
        }

        [Fact]
        public async Task The_Same_Packet_Cannot_Be_Used_Twice_By_Two_Requests_At_The_Same_Time()
        {
            var s = await SeedAsync();
            var packets = await CreatePacketsAsync(s, s.A.HospitalId, "O+", 2);
            var target = packets[0].PacketId;
            var inventoryId = (await s.Context.BloodInventories.SingleAsync(i => i.HospitalId == s.A.HospitalId)).InventoryId;

            // Two requests read the packet as Available before either saves
            using var first = s.NewContext();
            using var second = s.NewContext();
            await first.BloodPackets.FindAsync(target);
            await second.BloodPackets.FindAsync(target);

            var dto = new UpdateInventoryDto { MinimumThreshold = 0, MaximumCapacity = 100, AuditNotes = "Theatre", IssuePacketIds = new() { target } };
            await new BloodInventoryService(first).UpdateInventoryAsync(inventoryId, dto);

            var conflict = await Assert.ThrowsAsync<ConflictException>(() => new BloodInventoryService(second).UpdateInventoryAsync(inventoryId, dto));
            Assert.Equal(InventoryLedger.PacketConflictMessage, conflict.Message);

            // Only the first issue changed the packet (on PostgreSQL the losing save is one rolled-back transaction;
            // the in-memory test database has no transactions, so only the packet row is checked here)
            using var check = s.NewContext();
            var saved = await check.BloodPackets.AsNoTracking().SingleAsync(p => p.PacketId == target);
            Assert.Equal(BloodPacketStatus.Issued, saved.Status);
            Assert.Equal(1, saved.ConcurrencyToken);

            // A later attempt on fresh data is refused outright
            var late = await Assert.ThrowsAsync<InvalidOperationException>(() => new BloodInventoryService(check).UpdateInventoryAsync(inventoryId, dto));
            Assert.Contains("Issued", late.Message);
        }

        // ---------------- R5: hospital donations ----------------

        [Fact]
        public async Task Hospital_Acceptance_Holds_Packets_And_Never_Runs_The_Agent()
        {
            var s = await SeedAsync();
            var request = await AddApprovedRequestAsync(s, s.Patient.UserId, s.A.HospitalId, s.DoctorA.DoctorId);
            var packets = await CreatePacketsAsync(s, s.B.HospitalId, "A+", 3);

            var result = await s.Acceptances.AcceptAsHospitalAsync(s.StaffB.UserId, s.B.HospitalId,
                new CreateHospitalDonationDto { BloodRequestId = request.BloodRequestId, PacketIds = packets.Take(2).Select(p => p.PacketId).ToList() });

            Assert.Equal("Accepted", result.Status);
            Assert.Equal(s.B.HospitalId, result.DonorHospitalId);
            Assert.Equal(2, result.Packets.Count);
            Assert.Equal(1, await UnitsAsync(s.Context, s.B.HospitalId, "A+"));
            Assert.All(result.Packets, p => Assert.Equal(BloodPacketStatus.Reserved, p.Status));
            s.Planning.Verify(p => p.DispatchPlanAsync(It.IsAny<PlanRequestDto>()), Times.Never);

            // The screening agent can never pick it up
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Acceptances.UpdateScreeningStatusAsync(result.AcceptanceId, AcceptanceStatus.ScreeningPending));

            // The assigned doctor is told
            Assert.Single(s.Context.Notifications.Where(n => n.UserId == s.DoctorA.UserId && n.NotificationType == "HospitalDonationOffered"));
        }

        [Fact]
        public async Task A_Hospital_Cannot_Donate_To_Its_Own_Request_Or_More_Than_The_Free_Slots()
        {
            var s = await SeedAsync();
            var request = await AddApprovedRequestAsync(s, s.Patient.UserId, s.A.HospitalId, s.DoctorA.DoctorId, units: 2);
            var ownPackets = await CreatePacketsAsync(s, s.A.HospitalId, "A+", 1);
            var bPackets = await CreatePacketsAsync(s, s.B.HospitalId, "A+", 3);
            var bWrongGroup = await CreatePacketsAsync(s, s.B.HospitalId, "O+", 1);

            var own = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Acceptances.AcceptAsHospitalAsync(s.StaffA.UserId, s.A.HospitalId,
                new CreateHospitalDonationDto { BloodRequestId = request.BloodRequestId, PacketIds = ownPackets.Select(p => p.PacketId).ToList() }));
            Assert.Contains("own hospital", own.Message);

            var tooMany = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Acceptances.AcceptAsHospitalAsync(s.StaffB.UserId, s.B.HospitalId,
                new CreateHospitalDonationDto { BloodRequestId = request.BloodRequestId, PacketIds = bPackets.Select(p => p.PacketId).ToList() }));
            Assert.Contains("at most 2", tooMany.Message);

            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Acceptances.AcceptAsHospitalAsync(s.StaffB.UserId, s.B.HospitalId,
                new CreateHospitalDonationDto { BloodRequestId = request.BloodRequestId, PacketIds = bWrongGroup.Select(p => p.PacketId).ToList() }));

            Assert.Equal(3, await UnitsAsync(s.Context, s.B.HospitalId, "A+"));
        }

        [Fact]
        public async Task Only_The_Assigned_Doctor_Approves_And_The_Packets_Become_Donated()
        {
            var s = await SeedAsync();
            var request = await AddApprovedRequestAsync(s, s.Patient.UserId, s.A.HospitalId, s.DoctorA.DoctorId, units: 2);
            var packets = await CreatePacketsAsync(s, s.B.HospitalId, "A+", 3);
            var offer = await s.Acceptances.AcceptAsHospitalAsync(s.StaffB.UserId, s.B.HospitalId,
                new CreateHospitalDonationDto { BloodRequestId = request.BloodRequestId, PacketIds = packets.Take(2).Select(p => p.PacketId).ToList() });

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Acceptances.ApproveHospitalDonationAsync(offer.AcceptanceId, s.OtherDoctorA.UserId!.Value, null));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Acceptances.ApproveHospitalDonationAsync(offer.AcceptanceId, s.DoctorB.UserId!.Value, null));

            var approved = await s.Acceptances.ApproveHospitalDonationAsync(offer.AcceptanceId, s.DoctorA.UserId!.Value, "Cross-matched");

            Assert.Equal("Matched", approved.Status);
            var saved = await s.Context.BloodRequests.FindAsync(request.BloodRequestId);
            Assert.Equal(2, saved!.FulfilledUnits);
            Assert.Equal(BloodRequestStatus.Completed, saved.Status);
            Assert.Equal(2, await s.Context.RequestFulfillmentHistories.CountAsync(h => h.AcceptanceId == offer.AcceptanceId));

            // Kept at the donating hospital with status Donated; out of its available count
            var donated = (await s.Inventory.GetPacketsAsync(s.B.HospitalId, null, BloodPacketStatus.Donated, null, s.B.HospitalId)).ToList();
            Assert.Equal(2, donated.Count);
            Assert.Equal(1, await UnitsAsync(s.Context, s.B.HospitalId, "A+"));
            Assert.Single(s.Context.Notifications.Where(n => n.HospitalId == s.B.HospitalId && n.NotificationType == "HospitalDonationApproved"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Acceptances.ApproveHospitalDonationAsync(offer.AcceptanceId, s.DoctorA.UserId!.Value, null));
        }

        [Fact]
        public async Task A_Donation_To_A_Hospital_Request_Moves_The_Packets_Into_That_Hospitals_Stock()
        {
            var s = await SeedAsync();
            var request = await AddApprovedRequestAsync(s, s.StaffA.UserId, s.A.HospitalId, s.DoctorA.DoctorId, units: 1);
            var packet = Assert.Single(await CreatePacketsAsync(s, s.B.HospitalId, "A+", 1));
            var offer = await s.Acceptances.AcceptAsHospitalAsync(s.StaffB.UserId, s.B.HospitalId,
                new CreateHospitalDonationDto { BloodRequestId = request.BloodRequestId, PacketIds = new() { packet.PacketId } });

            await s.Acceptances.ApproveHospitalDonationAsync(offer.AcceptanceId, s.DoctorA.UserId!.Value, null);

            var moved = await s.Context.BloodPackets.FindAsync(packet.PacketId);
            Assert.Equal(s.A.HospitalId, moved!.HospitalId);
            Assert.Equal(BloodPacketStatus.Available, moved.Status);
            Assert.Equal(s.B.HospitalId, moved.CreatedByHospitalId);
            Assert.Equal(packet.TrackingNumber, moved.TrackingNumber);
            Assert.Equal(1, await UnitsAsync(s.Context, s.A.HospitalId, "A+"));
            Assert.Equal(0, await UnitsAsync(s.Context, s.B.HospitalId, "A+"));
        }

        [Fact]
        public async Task Rejection_Withdrawal_And_Request_Deletion_Return_The_Packets()
        {
            var s = await SeedAsync();
            var request = await AddApprovedRequestAsync(s, s.Patient.UserId, s.A.HospitalId, s.DoctorA.DoctorId, units: 3);
            var packets = await CreatePacketsAsync(s, s.B.HospitalId, "A+", 2);
            var ids = packets.Select(p => p.PacketId).ToList();
            CreateHospitalDonationDto Dto() => new() { BloodRequestId = request.BloodRequestId, PacketIds = ids };

            // Doctor rejects: a reason is required and the packets come back
            var offer = await s.Acceptances.AcceptAsHospitalAsync(s.StaffB.UserId, s.B.HospitalId, Dto());
            Assert.Equal(0, await UnitsAsync(s.Context, s.B.HospitalId, "A+"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Acceptances.RejectHospitalDonationAsync(offer.AcceptanceId, s.DoctorA.UserId!.Value, " "));
            var rejected = await s.Acceptances.RejectHospitalDonationAsync(offer.AcceptanceId, s.DoctorA.UserId!.Value, "Group mismatch on cross-match");
            Assert.Equal("Rejected", rejected.Status);
            Assert.Equal(2, await UnitsAsync(s.Context, s.B.HospitalId, "A+"));

            // Hospital withdraws
            var second = await s.Acceptances.AcceptAsHospitalAsync(s.StaffB.UserId, s.B.HospitalId, Dto());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Acceptances.WithdrawHospitalDonationAsync(second.AcceptanceId, s.A.HospitalId));
            await s.Acceptances.WithdrawHospitalDonationAsync(second.AcceptanceId, s.B.HospitalId);
            Assert.Equal(2, await UnitsAsync(s.Context, s.B.HospitalId, "A+"));

            // Deleting the request while an offer waits closes the offer and its held packets come straight back
            var third = await s.Acceptances.AcceptAsHospitalAsync(s.StaffB.UserId, s.B.HospitalId, Dto());
            Assert.Equal(0, await UnitsAsync(s.Context, s.B.HospitalId, "A+"));

            await new BloodRequestService(s.Context).DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);
            Assert.Equal(AcceptanceStatus.Cancelled, (await s.Context.Acceptances.FindAsync(third.AcceptanceId))!.Status);
            Assert.Equal(2, await UnitsAsync(s.Context, s.B.HospitalId, "A+"));
            Assert.All(ids, id => Assert.Equal(BloodPacketStatus.Available, s.Context.BloodPackets.Find(id)!.Status));
            Assert.All(ids, id => Assert.Null(s.Context.BloodPackets.Find(id)!.HeldForReferenceId));
            Assert.Single(s.Context.Notifications.Where(n => n.HospitalId == s.B.HospitalId && n.NotificationType == "BloodRequestDeleted"));
            // Inventory audit rows about the held/released packets stay unchanged
            Assert.NotEmpty(s.Context.InventoryTransactions.Where(t => t.TransactionType == TransactionType.Released));
        }

        [Fact]
        public async Task When_The_Assigned_Doctor_Was_Removed_Another_Doctor_Of_The_Hospital_May_Decide()
        {
            var s = await SeedAsync();
            var request = await AddApprovedRequestAsync(s, s.Patient.UserId, s.A.HospitalId, s.DoctorA.DoctorId, units: 1);
            var packet = Assert.Single(await CreatePacketsAsync(s, s.B.HospitalId, "A+", 1));
            var offer = await s.Acceptances.AcceptAsHospitalAsync(s.StaffB.UserId, s.B.HospitalId,
                new CreateHospitalDonationDto { BloodRequestId = request.BloodRequestId, PacketIds = new() { packet.PacketId } });

            var requests = new BloodRequestService(s.Context);

            // While the assigned doctor is active, only that doctor lists the request
            Assert.Empty(await requests.GetAssignedRequestsAsync(s.OtherDoctorA.UserId!.Value));

            var assignment = await s.Context.BloodRequestVerifications.SingleAsync(v => v.BloodRequestId == request.BloodRequestId);
            assignment.DoctorId = null; // doctor deleted
            await s.Context.SaveChangesAsync();

            // The doctors allowed to decide now see it on their dashboard; a doctor of another hospital does not
            var listed = Assert.Single(await requests.GetAssignedRequestsAsync(s.OtherDoctorA.UserId!.Value));
            Assert.Equal(request.BloodRequestId, listed.BloodRequestId);
            Assert.Equal(1, listed.PendingHospitalDonations);
            Assert.Equal("Removed doctor", listed.AssignedDoctorName);
            Assert.Empty(await requests.GetAssignedRequestsAsync(s.DoctorB.UserId!.Value));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Acceptances.ApproveHospitalDonationAsync(offer.AcceptanceId, s.DoctorB.UserId!.Value, null));
            var approved = await s.Acceptances.ApproveHospitalDonationAsync(offer.AcceptanceId, s.OtherDoctorA.UserId!.Value, null);
            Assert.Equal("Matched", approved.Status);

            // Nothing left to decide: it leaves the fallback doctor's list again
            Assert.Empty(await requests.GetAssignedRequestsAsync(s.OtherDoctorA.UserId!.Value));
        }

        // ---------------- R6: doctor on hospital blood requests ----------------

        private static CreateBloodRequestDto RequestDto(Guid hospitalId, Guid? doctorId) => new()
        {
            HospitalId = hospitalId, BloodGroup = "O-", UnitsRequired = 2, Reason = "Trauma", Priority = "Critical", DoctorId = doctorId
        };

        [Fact]
        public async Task A_Hospital_Request_Needs_One_Of_Its_Own_Active_Doctors_And_Goes_To_That_Doctor()
        {
            var s = await SeedAsync();
            var service = new BloodRequestService(s.Context);

            var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(s.StaffA.UserId, RequestDto(s.A.HospitalId, null)));
            Assert.Equal("Select the doctor who will approve this request.", missing.Message);

            var foreign = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(s.StaffA.UserId, RequestDto(s.A.HospitalId, s.DoctorB.DoctorId)));
            Assert.Equal("The selected doctor does not belong to your hospital.", foreign.Message);

            s.OtherDoctorA.IsActive = false;
            await s.Context.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(s.StaffA.UserId, RequestDto(s.A.HospitalId, s.OtherDoctorA.DoctorId)));
            Assert.Equal(0, await s.Context.BloodRequests.CountAsync());

            var created = await service.CreateRequestAsync(s.StaffA.UserId, RequestDto(s.A.HospitalId, s.DoctorA.DoctorId));
            Assert.Equal("Verified", created.Status);
            Assert.Equal(s.DoctorA.DoctorId, created.AssignedDoctorId);
            Assert.Single(await service.GetAssignedRequestsAsync(s.DoctorA.UserId!.Value));
        }

        [Fact]
        public async Task A_Patient_Request_Is_Unchanged_And_Ignores_A_Doctor()
        {
            var s = await SeedAsync();
            var created = await new BloodRequestService(s.Context).CreateRequestAsync(s.Patient.UserId, RequestDto(s.A.HospitalId, s.DoctorA.DoctorId));

            Assert.Equal("Pending", created.Status);
            Assert.Null(created.AssignedDoctorId);
            Assert.Empty(s.Context.BloodRequestVerifications);
        }
    }
}
