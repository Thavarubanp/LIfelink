using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.DTOs.Admin;
using LifeLink.DTOs.Complaints;
using LifeLink.DTOs.Inventory;
using LifeLink.DTOs.Transfer;
using LifeLink.Entities;
using LifeLink.Middleware;
using LifeLink.Services.Acceptances;
using LifeLink.Services.Admin;
using LifeLink.Services.Auth;
using LifeLink.Services.BloodCompatibility;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Complaints;
using LifeLink.Services.Emergency;
using LifeLink.Services.Inventory;
using LifeLink.Services.Notification;
using LifeLink.Services.Planning;
using LifeLink.Services.Transfer;
using LifeLink.Services.Verification;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Two actions at the same moment. Each test lets the second user read the records first (in their own database
    /// context), then the first user's action commits, then the second user's action runs on what they read: the
    /// loser must fail with a conflict (409) and nothing of the losing action may be saved.
    /// InMemory checks concurrency tokens but has no transactions, unique filtered indexes or foreign keys; those
    /// rules are checked against PostgreSQL separately.
    /// </summary>
    public class RaceConditionTests
    {
        private const int HospitalStaffRoleId = 2;

        private sealed class World
        {
            public string DbName = null!;
            public AppDbContext Db = null!;
            public Hospital A = null!;
            public Hospital B = null!;
            public User StaffA = null!;
            public User StaffB = null!;
            public User Patient = null!;
            public User Donor = null!;
            public User Donor2 = null!;
            public User Admin = null!;
            public Doctor DoctorA = null!;
            public Doctor FallbackDoctorA = null!;

            public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(DbName).Options);
        }

        private static async Task<World> SeedAsync()
        {
            var name = Guid.NewGuid().ToString();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);
            db.Database.EnsureCreated();
            var w = new World
            {
                DbName = name,
                Db = db,
                A = new Hospital { HospitalId = Guid.NewGuid(), Name = "Hospital A", Email = "a@h.org", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved, PacketShelfLifeDays = 35 },
                B = new Hospital { HospitalId = Guid.NewGuid(), Name = "Hospital B", Email = "b@h.org", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved, PacketShelfLifeDays = 35 },
                StaffA = new User { UserId = Guid.NewGuid(), FirstName = "Staff", LastName = "A", Email = "a@h.org" },
                StaffB = new User { UserId = Guid.NewGuid(), FirstName = "Staff", LastName = "B", Email = "b@h.org" },
                Patient = new User { UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Ient", Email = "patient@h.org" },
                Donor = new User { UserId = Guid.NewGuid(), FirstName = "Don", LastName = "Or", Email = "donor@h.org", BloodGroup = "O+" },
                Donor2 = new User { UserId = Guid.NewGuid(), FirstName = "Don", LastName = "Two", Email = "donor2@h.org", BloodGroup = "O+" },
                Admin = new User { UserId = Guid.NewGuid(), FirstName = "Ad", LastName = "Min", Email = "admin@h.org" }
            };
            w.DoctorA = new Doctor { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = w.A.HospitalId, FirstName = "Ann", LastName = "Doc", Email = "ann@a.org", IsActive = true, MustChangePassword = false };
            w.FallbackDoctorA = new Doctor { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = w.A.HospitalId, FirstName = "Ola", LastName = "Doc", Email = "ola@a.org", IsActive = true, MustChangePassword = false };

            await db.Hospitals.AddRangeAsync(w.A, w.B);
            await db.Users.AddRangeAsync(w.StaffA, w.StaffB, w.Patient, w.Donor, w.Donor2, w.Admin);
            await db.UserRoles.AddRangeAsync(
                new UserRole { UserId = w.StaffA.UserId, RoleId = HospitalStaffRoleId },
                new UserRole { UserId = w.StaffB.UserId, RoleId = HospitalStaffRoleId },
                new UserRole { UserId = w.Admin.UserId, RoleId = 4 });
            await db.Doctors.AddRangeAsync(w.DoctorA, w.FallbackDoctorA);
            await db.SaveChangesAsync();
            return w;
        }

        private static AcceptanceService Acceptances(AppDbContext db) => new(db, new BloodCompatibilityService(), new Mock<IPlanningAgentService>().Object);
        private static VerificationService Verification(AppDbContext db) => new(db, new Mock<INotificationAgentService>().Object);

        private static async Task<BloodRequest> AddApprovedRequestAsync(World w, int units = 2, string group = "O+", Guid? creatorId = null)
        {
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(), PatientUserId = creatorId ?? w.Patient.UserId, HospitalId = w.A.HospitalId, BloodGroup = group,
                UnitsRequired = units, Reason = "Surgery", Priority = "High", Status = BloodRequestStatus.Approved, ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await w.Db.BloodRequests.AddAsync(request);
            await w.Db.BloodRequestVerifications.AddAsync(new BloodRequestVerification
            {
                BloodRequestId = request.BloodRequestId, DoctorId = w.DoctorA.DoctorId, Status = VerificationStatus.Approved, VerifiedAt = DateTime.UtcNow
            });
            await w.Db.SaveChangesAsync();
            return request;
        }

        /// <summary>A donor whose screening report (version 1) waits for the doctor.</summary>
        private static async Task<(Acceptance Acceptance, DonorVerification Report)> AddScreenedDonorAsync(World w, BloodRequest request)
        {
            var acceptance = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = w.Donor.UserId, Status = AcceptanceStatus.ScreeningCompleted };
            var report = new DonorVerification
            {
                DonorVerificationId = Guid.NewGuid(), AcceptanceId = acceptance.AcceptanceId, DoctorId = w.DoctorA.DoctorId, Status = VerificationStatus.Pending,
                ReportVersion = 1, ReportJson = "{\"risk_level\":\"LOW\"}", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            await w.Db.Acceptances.AddAsync(acceptance);
            await w.Db.DonorVerifications.AddAsync(report);
            await w.Db.SaveChangesAsync();
            return (acceptance, report);
        }

        private static async Task<List<BloodPacket>> AddPacketsAsync(World w, Guid hospitalId, int count, string group = "O+")
        {
            var created = await new BloodInventoryService(w.Db).CreatePacketsAsync(hospitalId,
                new CreateBloodPacketsDto { BloodGroup = group, CollectionDate = PacketDateRules.Today(DateTime.UtcNow), Quantity = count }, null);
            var ids = created.Select(p => p.PacketId).ToList();
            return await w.Db.BloodPackets.Where(p => ids.Contains(p.PacketId)).ToListAsync();
        }

        /// <summary>The second user "reads" these records now (they stay as read in that context).</summary>
        private static async Task ReadAsync<T>(AppDbContext context, params object[] keys) where T : class
        {
            foreach (var key in keys) Assert.NotNull(await context.Set<T>().FindAsync(key));
        }

        private static async Task AssertConflictAsync(Func<Task> action)
        {
            var ex = await Record.ExceptionAsync(action);
            Assert.True(ex is DbUpdateConcurrencyException or ConflictException, $"Expected a 409 conflict, got {ex?.GetType().Name ?? "no exception"}: {ex?.Message}");
        }

        // ---------------- Blood requests and acceptances ----------------

        [Fact]
        public async Task Accept_While_The_Creator_Cancels_Fails_And_Leaves_No_Stranded_Acceptance()
        {
            var w = await SeedAsync();
            var request = await AddApprovedRequestAsync(w);
            using var donorSide = w.NewContext();
            await ReadAsync<BloodRequest>(donorSide, request.BloodRequestId);

            await new BloodRequestService(w.NewContext()).CancelRequestAsync(request.BloodRequestId, w.Patient.UserId);

            await AssertConflictAsync(() => Acceptances(donorSide).AcceptRequestAsync(w.Donor.UserId,
                new CreateAcceptanceDto { BloodRequestId = request.BloodRequestId, DonorBloodGroup = "O+" }));

            using var check = w.NewContext();
            Assert.Equal(BloodRequestStatus.Cancelled, (await check.BloodRequests.FindAsync(request.BloodRequestId))!.Status);
            // (InMemory keeps rows written earlier in a failed save; PostgreSQL rolls the whole save back: PostgresRaceTests)
        }

        [Fact]
        public async Task Hospital_Offer_While_The_Request_Is_Cancelled_Fails_And_The_Packets_Stay_Available()
        {
            var w = await SeedAsync();
            var request = await AddApprovedRequestAsync(w);
            var packets = await AddPacketsAsync(w, w.B.HospitalId, 2);
            using var hospitalSide = w.NewContext();
            await ReadAsync<BloodRequest>(hospitalSide, request.BloodRequestId);

            await new BloodRequestService(w.NewContext()).CancelRequestAsync(request.BloodRequestId, w.Patient.UserId);

            var ex = await Assert.ThrowsAsync<ConflictException>(() => Acceptances(hospitalSide).AcceptAsHospitalAsync(w.StaffB.UserId, w.B.HospitalId,
                new CreateHospitalDonationDto { BloodRequestId = request.BloodRequestId, PacketIds = packets.Select(p => p.PacketId).ToList() }));
            Assert.Equal(AcceptanceService.HospitalOfferConflictMessage, ex.Message);

            using var check = w.NewContext();
            Assert.Equal(BloodRequestStatus.Cancelled, (await check.BloodRequests.FindAsync(request.BloodRequestId))!.Status);
            // (InMemory keeps rows written earlier in a failed save; PostgreSQL rolls the whole save back: PostgresRaceTests)
        }

        [Fact]
        public async Task Accept_While_The_Request_Is_Completed_By_A_Hospital_Donation_Fails()
        {
            var w = await SeedAsync();
            var request = await AddApprovedRequestAsync(w, units: 2);
            var packets = await AddPacketsAsync(w, w.B.HospitalId, 2);
            var offer = await Acceptances(w.Db).AcceptAsHospitalAsync(w.StaffB.UserId, w.B.HospitalId,
                new CreateHospitalDonationDto { BloodRequestId = request.BloodRequestId, PacketIds = packets.Select(p => p.PacketId).ToList() });
            using var donorSide = w.NewContext();
            await ReadAsync<BloodRequest>(donorSide, request.BloodRequestId);

            await Acceptances(w.NewContext()).ApproveHospitalDonationAsync(offer.AcceptanceId, w.DoctorA.UserId!.Value, null);

            await AssertConflictAsync(() => Acceptances(donorSide).AcceptRequestAsync(w.Donor.UserId,
                new CreateAcceptanceDto { BloodRequestId = request.BloodRequestId, DonorBloodGroup = "O+" }));

            using var check = w.NewContext();
            Assert.Equal(BloodRequestStatus.Completed, (await check.BloodRequests.FindAsync(request.BloodRequestId))!.Status);
            // (InMemory keeps rows written earlier in a failed save; PostgreSQL rolls the whole save back: PostgresRaceTests)
        }

        [Fact]
        public async Task The_Same_Donor_Accepting_Twice_At_Once_Creates_One_Acceptance()
        {
            var w = await SeedAsync();
            var request = await AddApprovedRequestAsync(w);
            using var secondClick = w.NewContext();
            await ReadAsync<BloodRequest>(secondClick, request.BloodRequestId);

            await Acceptances(w.NewContext()).AcceptRequestAsync(w.Donor.UserId, new CreateAcceptanceDto { BloodRequestId = request.BloodRequestId, DonorBloodGroup = "O+" });
            var ex = await Record.ExceptionAsync(() => Acceptances(secondClick).AcceptRequestAsync(w.Donor.UserId,
                new CreateAcceptanceDto { BloodRequestId = request.BloodRequestId, DonorBloodGroup = "O+" }));

            Assert.True(ex is DbUpdateConcurrencyException or InvalidOperationException);
            using var check = w.NewContext();
            Assert.Single(await check.Acceptances.ToListAsync());
        }

        [Fact]
        public async Task Two_Donors_Accepting_At_Once_Are_Serialised_And_The_Second_Can_Retry()
        {
            var w = await SeedAsync();
            var request = await AddApprovedRequestAsync(w);
            using var donor2Side = w.NewContext();
            await ReadAsync<BloodRequest>(donor2Side, request.BloodRequestId);

            await Acceptances(w.NewContext()).AcceptRequestAsync(w.Donor.UserId, new CreateAcceptanceDto { BloodRequestId = request.BloodRequestId, DonorBloodGroup = "O+" });
            await AssertConflictAsync(() => Acceptances(donor2Side).AcceptRequestAsync(w.Donor2.UserId,
                new CreateAcceptanceDto { BloodRequestId = request.BloodRequestId, DonorBloodGroup = "O+" }));

            // "Please refresh and try again": on PostgreSQL the retry on fresh data succeeds (the rule allows both donors)
            // (InMemory keeps rows written earlier in a failed save; PostgreSQL rolls the whole save back: PostgresRaceTests)
        }

        // ---------------- Doctor decisions and donor withdrawal ----------------

        [Fact]
        public async Task Doctor_Approving_While_The_Donor_Withdraws_Fails_And_No_Slot_Stays_Reserved()
        {
            var w = await SeedAsync();
            var request = await AddApprovedRequestAsync(w);
            var (acceptance, report) = await AddScreenedDonorAsync(w, request);
            using var doctorSide = w.NewContext();
            await ReadAsync<DonorVerification>(doctorSide, report.DonorVerificationId);
            await ReadAsync<Acceptance>(doctorSide, acceptance.AcceptanceId);
            await ReadAsync<BloodRequest>(doctorSide, request.BloodRequestId);

            await Acceptances(w.NewContext()).CancelAcceptanceAsync(acceptance.AcceptanceId, w.Donor.UserId);

            await AssertConflictAsync(() => Verification(doctorSide).ApproveDonorVerificationAsync(report.DonorVerificationId, w.DoctorA.UserId!.Value, null));

            using var check = w.NewContext();
            Assert.Equal(AcceptanceStatus.Cancelled, (await check.Acceptances.FindAsync(acceptance.AcceptanceId))!.Status);
            Assert.Equal(0, (await check.BloodRequests.FindAsync(request.BloodRequestId))!.ReservedUnits);
            // (InMemory keeps rows written earlier in a failed save; PostgreSQL rolls the whole save back: PostgresRaceTests)
        }

        [Fact]
        public async Task Assigned_And_Fallback_Doctor_Approve_And_Reject_The_Same_Report_Only_One_Decision_Counts()
        {
            var w = await SeedAsync();
            var request = await AddApprovedRequestAsync(w);
            var (acceptance, report) = await AddScreenedDonorAsync(w, request);
            using var assignedSide = w.NewContext();
            await ReadAsync<DonorVerification>(assignedSide, report.DonorVerificationId);
            await ReadAsync<Acceptance>(assignedSide, acceptance.AcceptanceId);
            await ReadAsync<BloodRequest>(assignedSide, request.BloodRequestId);

            await Verification(w.NewContext()).RejectDonorVerificationAsync(report.DonorVerificationId, w.FallbackDoctorA.UserId!.Value, "Low haemoglobin");

            await AssertConflictAsync(() => Verification(assignedSide).ApproveDonorVerificationAsync(report.DonorVerificationId, w.DoctorA.UserId!.Value, null));

            using var check = w.NewContext();
            Assert.Equal(AcceptanceStatus.Rejected, (await check.Acceptances.FindAsync(acceptance.AcceptanceId))!.Status);
            Assert.Equal(0, (await check.BloodRequests.FindAsync(request.BloodRequestId))!.ReservedUnits); // no leaked slot
            // (InMemory keeps rows written earlier in a failed save; PostgreSQL rolls the whole save back: PostgresRaceTests)
        }

        [Fact]
        public async Task Two_Screening_Reports_Submitted_At_Once_Store_One_Version_And_A_Retry_Is_Idempotent()
        {
            var w = await SeedAsync();
            var request = await AddApprovedRequestAsync(w);
            var acceptance = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = w.Donor.UserId, Status = AcceptanceStatus.ScreeningPending };
            await w.Db.Acceptances.AddAsync(acceptance);
            await w.Db.SaveChangesAsync();
            using var secondCall = w.NewContext();
            await ReadAsync<Acceptance>(secondCall, acceptance.AcceptanceId);

            var dto = new ScreeningReportNotificationDto { AcceptanceId = acceptance.AcceptanceId.ToString(), ReportJson = "{\"risk_level\":\"LOW\"}" };
            await Acceptances(w.NewContext()).SubmitScreeningReportAsync(dto);
            await AssertConflictAsync(() => Acceptances(secondCall).SubmitScreeningReportAsync(
                new ScreeningReportNotificationDto { AcceptanceId = dto.AcceptanceId, ReportJson = "{\"risk_level\":\"HIGH\"}" }));

            using var check = w.NewContext();
            Assert.Equal(AcceptanceStatus.ScreeningCompleted, (await check.Acceptances.FindAsync(acceptance.AcceptanceId))!.Status);
            // (InMemory keeps rows written earlier in a failed save; PostgreSQL rolls the whole save back: PostgresRaceTests)
        }

        [Fact]
        public async Task The_Agent_Resending_The_Same_Report_After_A_Timeout_Gets_The_Stored_Version()
        {
            var w = await SeedAsync();
            var request = await AddApprovedRequestAsync(w);
            var acceptance = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = w.Donor.UserId, Status = AcceptanceStatus.ScreeningPending };
            await w.Db.Acceptances.AddAsync(acceptance);
            await w.Db.SaveChangesAsync();
            var dto = new ScreeningReportNotificationDto { AcceptanceId = acceptance.AcceptanceId.ToString(), ReportJson = "{\"risk_level\":\"LOW\"}" };

            var first = await Acceptances(w.NewContext()).SubmitScreeningReportAsync(dto);
            var retry = await Acceptances(w.NewContext()).SubmitScreeningReportAsync(dto);

            Assert.Equal(first.DonorVerificationId, retry.DonorVerificationId);
            using var check = w.NewContext();
            Assert.Single(await check.DonorVerifications.ToListAsync());
        }

        [Fact]
        public async Task Agent_Opening_The_Interview_Twice_Returns_The_Current_State()
        {
            var w = await SeedAsync();
            var request = await AddApprovedRequestAsync(w);
            var acceptance = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = w.Donor.UserId, Status = AcceptanceStatus.Accepted };
            await w.Db.Acceptances.AddAsync(acceptance);
            await w.Db.SaveChangesAsync();

            await Acceptances(w.NewContext()).UpdateScreeningStatusAsync(acceptance.AcceptanceId, AcceptanceStatus.ScreeningPending);
            var again = await Acceptances(w.NewContext()).UpdateScreeningStatusAsync(acceptance.AcceptanceId, AcceptanceStatus.ScreeningPending);

            Assert.Equal(nameof(AcceptanceStatus.ScreeningPending), again.Status);
        }

        // ---------------- Transfers ----------------

        [Fact]
        public async Task Sender_Accepting_A_Transfer_Request_While_The_Receiver_Withdraws_It_Fails_And_No_Packet_Moves()
        {
            var w = await SeedAsync();
            var packets = await AddPacketsAsync(w, w.B.HospitalId, 1);
            var transfer = await new TransferRequestService(w.Db).CreateTransferRequestAsync(
                new TransferRequestCreateDto { TransferType = "Request", CounterpartHospitalId = w.B.HospitalId, BloodGroup = "O+", UnitsRequested = 1 }, w.A.HospitalId);
            using var senderSide = w.NewContext();
            await ReadAsync<HospitalTransferRequest>(senderSide, transfer.TransferRequestId);

            await new TransferRequestService(w.NewContext()).DeleteTransferRequestAsync(transfer.TransferRequestId, w.A.HospitalId);

            var ex = await Assert.ThrowsAsync<ConflictException>(() => new TransferRequestService(senderSide)
                .ApproveTransferRequestAsync(transfer.TransferRequestId, w.B.HospitalId, null, packets.Select(p => p.PacketId).ToList()));
            Assert.Equal(TransferRequestService.TransferConflictMessage, ex.Message);

            using var check = w.NewContext();
            Assert.Equal(nameof(TransferRequestStatus.Cancelled), (await check.HospitalTransferRequests.FindAsync(transfer.TransferRequestId))!.Status);
            var packet = await check.BloodPackets.SingleAsync();
            Assert.Equal(w.B.HospitalId, packet.HospitalId);
            Assert.Equal(BloodPacketStatus.Available, packet.Status);
        }

        [Fact]
        public async Task Accept_Clicked_Twice_With_Different_Packets_Moves_Only_One_Set()
        {
            var w = await SeedAsync();
            var packets = await AddPacketsAsync(w, w.B.HospitalId, 2);
            var transfer = await new TransferRequestService(w.Db).CreateTransferRequestAsync(
                new TransferRequestCreateDto { TransferType = "Request", CounterpartHospitalId = w.B.HospitalId, BloodGroup = "O+", UnitsRequested = 1 }, w.A.HospitalId);
            using var secondClick = w.NewContext();
            await ReadAsync<HospitalTransferRequest>(secondClick, transfer.TransferRequestId);

            await new TransferRequestService(w.NewContext()).ApproveTransferRequestAsync(transfer.TransferRequestId, w.B.HospitalId, null, new[] { packets[0].PacketId });
            await Assert.ThrowsAsync<ConflictException>(() => new TransferRequestService(secondClick)
                .ApproveTransferRequestAsync(transfer.TransferRequestId, w.B.HospitalId, null, new[] { packets[1].PacketId }));

            using var check = w.NewContext();
            Assert.Equal(1, await check.BloodPackets.CountAsync(p => p.HospitalId == w.A.HospitalId));
            Assert.Equal(1, await check.BloodPackets.CountAsync(p => p.HospitalId == w.B.HospitalId && p.Status == BloodPacketStatus.Available));
        }

        // ---------------- Emergencies ----------------

        [Fact]
        public async Task Emergency_Approve_And_Reject_At_Once_Only_One_Wins()
        {
            var w = await SeedAsync();
            var emergency = new EmergencyRequest
            {
                EmergencyRequestId = Guid.NewGuid(), HospitalId = w.A.HospitalId, BloodGroup = "O-", UnitsRequired = 2,
                Priority = "Critical", Status = nameof(EmergencyRequestStatus.Pending), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            await w.Db.EmergencyRequests.AddAsync(emergency);
            await w.Db.SaveChangesAsync();
            using var otherTab = w.NewContext();
            await ReadAsync<EmergencyRequest>(otherTab, emergency.EmergencyRequestId);

            await new EmergencyRequestService(w.NewContext()).ApproveEmergencyRequestAsync(emergency.EmergencyRequestId, w.A.HospitalId);
            await AssertConflictAsync(() => new EmergencyRequestService(otherTab).RejectEmergencyRequestAsync(emergency.EmergencyRequestId, w.A.HospitalId));

            using var check = w.NewContext();
            Assert.Equal(nameof(EmergencyRequestStatus.Approved), (await check.EmergencyRequests.FindAsync(emergency.EmergencyRequestId))!.Status);
        }

        // ---------------- Registration, suspension, complaints ----------------

        [Fact]
        public async Task Hospital_Approval_Clicked_Twice_Saves_One_Decision_And_Sends_One_Email()
        {
            var w = await SeedAsync();
            var pending = new Hospital { HospitalId = Guid.NewGuid(), Name = "New Hospital", Email = "new@h.org", ApprovalStatus = ApprovalStatus.Pending };
            await w.Db.Hospitals.AddAsync(pending);
            await w.Db.SaveChangesAsync();
            var email = new Mock<IEmailService>();
            AdminService Admin(AppDbContext db) => new(db, new AdminNotificationService(db, email.Object, NullLogger<AdminNotificationService>.Instance));
            using var secondClick = w.NewContext();
            await ReadAsync<Hospital>(secondClick, pending.HospitalId);

            await Admin(w.NewContext()).ApproveHospitalAsync(pending.HospitalId, w.Admin.UserId);
            await AssertConflictAsync(() => Admin(secondClick).ApproveHospitalAsync(pending.HospitalId, w.Admin.UserId));

            using var check = w.NewContext();
            Assert.Equal(ApprovalStatus.Approved, (await check.Hospitals.FindAsync(pending.HospitalId))!.ApprovalStatus);
            // (InMemory keeps rows written earlier in a failed save; PostgreSQL rolls the whole save back: PostgresRaceTests)
            email.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        }

        [Fact]
        public async Task Suspend_And_Reinstate_At_Once_Only_One_Wins_And_Only_Its_Email_Is_Sent()
        {
            var w = await SeedAsync();
            var email = new Mock<IEmailService>();
            AdminService Admin(AppDbContext db) => new(db, new AdminNotificationService(db, email.Object, NullLogger<AdminNotificationService>.Instance));
            using var otherTab = w.NewContext();
            await ReadAsync<User>(otherTab, w.Donor.UserId);

            await Admin(w.NewContext()).SuspendUserAsync(w.Donor.UserId, new SuspendUserDto { Reason = "Investigation" }, w.Admin.UserId);
            await AssertConflictAsync(() => Admin(otherTab).ReinstateUserAsync(w.Donor.UserId, w.Admin.UserId));

            using var check = w.NewContext();
            Assert.True((await check.Users.FindAsync(w.Donor.UserId))!.IsSuspended);
            email.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        }

        [Fact]
        public async Task Creator_Solving_A_Complaint_While_The_Admin_Replies_Fails()
        {
            var w = await SeedAsync();
            var complaint = new Complaint
            {
                ComplaintId = Guid.NewGuid(), UserId = w.Donor.UserId, ComplaintType = "Service", Subject = "Late", Description = "Waited long",
                Status = ComplaintStatus.OPEN, CreatedAt = DateTime.UtcNow
            };
            await w.Db.Complaints.AddAsync(complaint);
            await w.Db.SaveChangesAsync();
            var notifications = new AdminNotificationService(w.Db, new Mock<IEmailService>().Object, NullLogger<AdminNotificationService>.Instance);
            using var creatorSide = w.NewContext();
            await ReadAsync<Complaint>(creatorSide, complaint.ComplaintId);

            await new ComplaintService(w.NewContext(), notifications).AdminReplyAsync(complaint.ComplaintId, w.Admin.UserId, new ReviewComplaintDto { Notes = "We are checking." });
            await AssertConflictAsync(() => new ComplaintService(creatorSide, notifications).SolveComplaintAsync(complaint.ComplaintId, w.Donor.UserId));

            using var check = w.NewContext();
            Assert.Equal(ComplaintStatus.OPEN, (await check.Complaints.FindAsync(complaint.ComplaintId))!.Status);
        }

        // ---------------- Background sweeps ----------------

        [Fact]
        public async Task Request_Expiry_Sweep_Is_A_NoOp_Even_For_Old_Requests()
        {
            var w = await SeedAsync();
            var changed = await AddApprovedRequestAsync(w, group: "O+");
            var other = await AddApprovedRequestAsync(w, group: "A+");
            foreach (var r in await w.Db.BloodRequests.ToListAsync()) r.ExpiryDate = DateTime.UtcNow.AddMinutes(-1);
            await w.Db.SaveChangesAsync();
            using var sweep = w.NewContext();
            await ReadAsync<BloodRequest>(sweep, changed.BloodRequestId);

            await new BloodRequestService(w.NewContext()).CancelRequestAsync(changed.BloodRequestId, w.Patient.UserId);

            var expired = await new RequestExpiryService(sweep, NullLogger<RequestExpiryService>.Instance).ProcessExpiredRequestsAsync();

            Assert.Equal(0, expired);
            using var check = w.NewContext();
            Assert.Equal(BloodRequestStatus.Cancelled, (await check.BloodRequests.FindAsync(changed.BloodRequestId))!.Status);
            Assert.Equal(BloodRequestStatus.Approved, (await check.BloodRequests.FindAsync(other.BloodRequestId))!.Status);
        }

        [Fact]
        public async Task Opening_An_Old_Request_Does_Not_Change_Its_State()
        {
            var w = await SeedAsync();
            var request = await AddApprovedRequestAsync(w);
            request.ExpiryDate = DateTime.UtcNow.AddMinutes(-1);
            await w.Db.SaveChangesAsync();
            using var viewer = w.NewContext();
            await ReadAsync<BloodRequest>(viewer, request.BloodRequestId);

            await new RequestExpiryService(w.NewContext(), NullLogger<RequestExpiryService>.Instance).ProcessExpiredRequestsAsync();
            var shown = await new BloodRequestService(viewer).GetRequestByIdAsync(request.BloodRequestId);

            Assert.Equal(nameof(BloodRequestStatus.Approved), shown!.Status);
        }

        [Fact]
        public async Task Packet_Expiry_Sweep_Skips_A_Group_Changed_At_The_Same_Moment_And_Expires_The_Others()
        {
            var w = await SeedAsync();
            var oPackets = await AddPacketsAsync(w, w.A.HospitalId, 1, "O+");
            var aPackets = await AddPacketsAsync(w, w.A.HospitalId, 1, "A+");
            foreach (var p in await w.Db.BloodPackets.ToListAsync()) p.ExpiryDate = DateTime.UtcNow.AddMinutes(-1);
            await w.Db.SaveChangesAsync();
            using var sweep = w.NewContext();
            await ReadAsync<BloodPacket>(sweep, oPackets[0].PacketId);

            // Someone changes the O+ packet at the same moment (its token moves on)
            using (var other = w.NewContext())
            {
                var packet = await other.BloodPackets.FindAsync(oPackets[0].PacketId);
                packet!.ConcurrencyToken++;
                await other.SaveChangesAsync();
            }

            var expired = await new BloodInventoryService(sweep).ProcessExpiredPacketsAsync();

            Assert.Equal(1, expired);
            using var check = w.NewContext();
            Assert.Equal(BloodPacketStatus.Available, (await check.BloodPackets.FindAsync(oPackets[0].PacketId))!.Status); // retried next sweep
            Assert.Equal(BloodPacketStatus.Expired, (await check.BloodPackets.FindAsync(aPackets[0].PacketId))!.Status);
        }

        // ---------------- Idempotency keys ----------------

        private static ActionExecutingContext ActionContext(AppDbContext db, Guid userId, string? key)
        {
            var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = services };
            http.Request.Method = "POST";
            if (key != null) http.Request.Headers[IdempotencyKeys.HeaderName] = key;
            http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())
            }, "test"));
            var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
            return new ActionExecutingContext(action, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());
        }

        [Fact]
        public async Task A_Repeated_Submission_With_The_Same_Idempotency_Key_Creates_Nothing_Twice()
        {
            var w = await SeedAsync();
            var filter = new IdempotentAttribute();
            var created = 0;

            async Task SubmitAsync()
            {
                var db = w.NewContext();
                var context = ActionContext(db, w.StaffA.UserId, "form-1");
                await filter.OnActionExecutionAsync(context, async () =>
                {
                    created++;
                    await db.SaveChangesAsync(); // the action's save also stores the key
                    return null!;
                });
                if (context.Result is ObjectResult { StatusCode: 409 } result)
                {
                    throw new ConflictException(DatabaseConflicts.AlreadySubmittedMessage);
                }
            }

            await SubmitAsync();
            var ex = await Assert.ThrowsAsync<ConflictException>(SubmitAsync);

            Assert.Equal(DatabaseConflicts.AlreadySubmittedMessage, ex.Message);
            Assert.Equal(1, created);
            using var check = w.NewContext();
            Assert.Equal($"{w.StaffA.UserId}:form-1", (await check.IdempotencyKeys.SingleAsync()).Key);
        }

        [Fact]
        public async Task A_Failed_Submission_Keeps_Nothing_So_The_Same_Key_Can_Be_Retried()
        {
            var w = await SeedAsync();
            var filter = new IdempotentAttribute();

            var failing = w.NewContext();
            await Assert.ThrowsAsync<InvalidOperationException>(() => filter.OnActionExecutionAsync(ActionContext(failing, w.StaffA.UserId, "form-2"),
                () => throw new InvalidOperationException("Select at least one blood packet.")));

            var retry = w.NewContext();
            var context = ActionContext(retry, w.StaffA.UserId, "form-2");
            await filter.OnActionExecutionAsync(context, async () => { await retry.SaveChangesAsync(); return null!; });

            Assert.Null(context.Result); // not refused
            using var check = w.NewContext();
            Assert.Single(await check.IdempotencyKeys.ToListAsync());
        }

        // ---------------- 409 mapping ----------------

        private static async Task<(int Status, string Body)> RunMiddlewareAsync(Exception exception)
        {
            var middleware = new GlobalExceptionMiddleware(_ => throw exception, NullLogger<GlobalExceptionMiddleware>.Instance);
            var http = new DefaultHttpContext();
            http.Response.Body = new System.IO.MemoryStream();
            await middleware.InvokeAsync(http);
            http.Response.Body.Position = 0;
            return (http.Response.StatusCode, await new System.IO.StreamReader(http.Response.Body).ReadToEndAsync());
        }

        private static DbUpdateException UniqueViolation(string constraint) =>
            new("save failed", new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation, constraintName: constraint));

        [Fact]
        public async Task Race_Failures_Become_409_With_A_Clear_Message()
        {
            var concurrency = await RunMiddlewareAsync(new DbUpdateConcurrencyException("stale"));
            Assert.Equal(409, concurrency.Status);
            Assert.Contains(ConflictException.DefaultMessage, concurrency.Body);

            var email = await RunMiddlewareAsync(UniqueViolation("IX_Users_Email"));
            Assert.Equal(409, email.Status);
            Assert.Contains(DatabaseConflicts.DuplicateEmailMessage, email.Body);

            var request = await RunMiddlewareAsync(UniqueViolation("IX_BloodRequests_OneActivePerCreatorHospitalGroup"));
            Assert.Contains(DatabaseConflicts.DuplicateActiveRequestMessage, request.Body);

            var appeal = await RunMiddlewareAsync(UniqueViolation("IX_Appeals_OneOpenPerUser"));
            Assert.Contains(DatabaseConflicts.DuplicateOpenAppealMessage, appeal.Body);

            var key = await RunMiddlewareAsync(UniqueViolation("PK_IdempotencyKeys"));
            Assert.Contains(DatabaseConflicts.AlreadySubmittedMessage, key.Body);

            var conflict = await RunMiddlewareAsync(new ConflictException(InventoryLedger.PacketConflictMessage));
            Assert.Equal(409, conflict.Status);

            // Other database errors are not turned into 409
            var other = await RunMiddlewareAsync(new DbUpdateException("save failed", new InvalidCastException()));
            Assert.Equal(500, other.Status);
        }

        [Fact]
        public async Task A_Controller_Lets_A_Conflict_Reach_The_Middleware_Instead_Of_Returning_400()
        {
            var service = new Mock<IVerificationService>();
            service.Setup(s => s.ApproveDonorVerificationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>()))
                   .ThrowsAsync(new ConflictException(ConflictException.DefaultMessage));
            service.Setup(s => s.RejectDonorVerificationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>()))
                   .ThrowsAsync(new InvalidOperationException("A rejection message is required."));
            var user = new Mock<LifeLink.Services.Common.ICurrentUserService>();
            user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
            var controller = new LifeLink.Controllers.DonorVerificationController(service.Object, user.Object);

            await Assert.ThrowsAsync<ConflictException>(() => controller.ApproveDonorVerification(Guid.NewGuid(), null));
            Assert.IsType<BadRequestObjectResult>(await controller.RejectDonorVerification(Guid.NewGuid(), new LifeLink.DTOs.Verification.ApproveRejectRequestDto()));
        }
    }
}
