using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.ActivityLogs;
using LifeLink.DTOs.Admin;
using LifeLink.DTOs.Auth;
using LifeLink.DTOs.BloodRequests;
using LifeLink.DTOs.Doctors;
using LifeLink.DTOs.Inventory;
using LifeLink.Entities;
using LifeLink.Services.Admin;
using LifeLink.Services.Auth;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Common;
using LifeLink.Services.Doctors;
using LifeLink.Services.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>Phase 3A: activity log (recording, visibility, filters, paging) and the admin badge counts.</summary>
    public class ActivityLogAndAttentionTests
    {
        private sealed class World
        {
            public AppDbContext Db = null!;
            public Hospital Hospital = null!;
            public User Admin = null!;
            public User Patient = null!;
            public User Staff = null!;
            public User DoctorLogin = null!;
            public Doctor Doctor = null!;
        }

        private static async Task<World> SeedAsync()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Database.EnsureCreated(); // seeds the roles (1 User, 2 HospitalStaff, 3 Doctor, 4 Admin)
            var w = new World
            {
                Db = db,
                Hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Test Hospital A", Email = "hospital.a@example.test", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved, PacketShelfLifeDays = 35, ExpiryAlertDays = 7 },
                Admin = new User { UserId = Guid.NewGuid(), FirstName = "Test", LastName = "Admin", Email = "admin@example.test" },
                Patient = new User { UserId = Guid.NewGuid(), FirstName = "Test", LastName = "Patient", Email = "patient@example.test" },
                Staff = new User { UserId = Guid.NewGuid(), FirstName = "Hospital", LastName = "Staff", Email = "hospital.a@example.test" },
                DoctorLogin = new User { UserId = Guid.NewGuid(), FirstName = "Test", LastName = "Doctor", Email = "doctor@example.test" }
            };
            w.Doctor = new Doctor { DoctorId = Guid.NewGuid(), HospitalId = w.Hospital.HospitalId, UserId = w.DoctorLogin.UserId, FirstName = "Test", LastName = "Doctor", Email = "doctor@example.test", LicenseNumber = "SLMC/T1", IsActive = true, MustChangePassword = false };
            db.Hospitals.Add(w.Hospital);
            db.Users.AddRange(w.Admin, w.Patient, w.Staff, w.DoctorLogin);
            db.UserRoles.AddRange(
                new UserRole { UserId = w.Admin.UserId, RoleId = 4 }, new UserRole { UserId = w.Patient.UserId, RoleId = 1 },
                new UserRole { UserId = w.Staff.UserId, RoleId = 2 }, new UserRole { UserId = w.DoctorLogin.UserId, RoleId = 3 });
            db.Doctors.Add(w.Doctor);
            await db.SaveChangesAsync();
            return w;
        }

        private static CreateBloodRequestDto Request(World w, string group = "A+") => new()
        {
            HospitalId = w.Hospital.HospitalId, BloodGroup = group, UnitsRequired = 1, Reason = "Test", Priority = "Normal"
        };

        private static AdminService Admin(World w) =>
            new(w.Db, new AdminNotificationService(w.Db, new Mock<IEmailService>().Object, NullLogger<AdminNotificationService>.Instance));

        private static Task<ActivityLogPageDto> Page(World w, IQueryable<ActivityLog> scope, ActivityLogQueryDto? q = null, bool admin = true) =>
            ActivityLogQueries.PageAsync(w.Db, scope, q ?? new ActivityLogQueryDto(),
                new ActivityLogViewerContext(admin ? w.Admin.UserId : w.Patient.UserId,
                    new HashSet<string> { admin ? "Admin" : "User" }, null));

        // ---------- Recording ----------

        [Fact]
        public async Task An_Action_Is_Logged_In_The_Same_Save_And_A_Failed_Action_Logs_Nothing()
        {
            var w = await SeedAsync();
            var service = new BloodRequestService(w.Db);

            var created = await service.CreateRequestAsync(w.Patient.UserId, Request(w));
            var entry = Assert.Single(w.Db.ActivityLogs);
            Assert.Equal("BloodRequest.Created", entry.Action);
            Assert.Equal(ActivityLogger.Types.BloodRequest, entry.EntityType);
            Assert.Equal(created.BloodRequestId, entry.EntityId);
            Assert.Equal(w.Patient.UserId, entry.ActorUserId);
            Assert.Equal("User", entry.ActorRole);
            Assert.Equal("Test Patient", entry.ActorName);

            // A duplicate active request is refused: nothing is logged for it
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(w.Patient.UserId, Request(w)));
            w.Db.ChangeTracker.Clear();
            Assert.Single(w.Db.ActivityLogs);
        }

        [Fact]
        public async Task Entries_Cannot_Be_Changed_Or_Deleted()
        {
            var w = await SeedAsync();
            await new BloodRequestService(w.Db).CreateRequestAsync(w.Patient.UserId, Request(w));
            var entry = await w.Db.ActivityLogs.SingleAsync();

            entry.Summary = "changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Db.SaveChangesAsync());
            w.Db.ChangeTracker.Clear();

            w.Db.ActivityLogs.Remove(await w.Db.ActivityLogs.SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Db.SaveChangesAsync());
        }

        [Fact]
        public async Task Hospital_And_Doctor_Actions_Belong_To_The_Hospitals_Log()
        {
            var w = await SeedAsync();
            // Hospital staff add packets (hospital action), then add a doctor
            await new BloodInventoryService(w.Db).CreatePacketsAsync(w.Hospital.HospitalId,
                new CreateBloodPacketsDto { BloodGroup = "O+", Quantity = 2, CollectionDate = DateOnly.FromDateTime(DateTime.UtcNow) }, w.Staff.UserId);
            await new DoctorService(w.Db, new PasswordHasherService()).CreateDoctorAsync(new CreateDoctorDto
            {
                HospitalId = w.Hospital.HospitalId, FirstName = "Second", LastName = "Doctor", Email = "doctor2@example.test", Password = "Passw0rd!", PhoneNumber = "0712345678", LicenseNumber = "SLMC/T2"
            });
            // The doctor changes their profile (doctor action, also in the hospital's log)
            await ActivityLogger.AddAsync(w.Db, w.DoctorLogin.UserId, "Profile.Updated", ActivityLogger.Types.Account, w.Doctor.DoctorId, "Updated their doctor profile.");
            await w.Db.SaveChangesAsync();

            var log = await Page(w, ActivityLogQueries.ForHospital(w.Db, w.Hospital.HospitalId));
            Assert.Equal(3, log.Total);
            Assert.Contains(log.Items, i => i.Action == "Inventory.PacketsAdded" && i.ActorRole == "HospitalStaff" && i.ActorName == "Test Hospital A");
            Assert.Contains(log.Items, i => i.Action == "Doctor.Added" && i.Summary.Contains("Dr. Second Doctor"));
            Assert.Contains(log.Items, i => i.Action == "Profile.Updated" && i.ActorRole == "Doctor" && i.ActorName == "Dr. Test Doctor");
            // The patient's own log has none of these
            Assert.Equal(0, (await Page(w, ActivityLogQueries.ForUser(w.Db, w.Patient.UserId))).Total);
        }

        [Fact]
        public async Task Admin_Actions_Appear_In_The_Subjects_Log_With_The_Admin_Name_Hidden_From_Them()
        {
            var w = await SeedAsync();
            await Admin(w).SuspendUserAsync(w.Patient.UserId, new SuspendUserDto { Reason = "Test reason" }, w.Admin.UserId);
            await Admin(w).SuspendHospitalAsync(w.Hospital.HospitalId, new SuspendHospitalDto { Reason = "Audit" }, w.Admin.UserId);

            var forUserAsAdmin = await Page(w, ActivityLogQueries.ForUser(w.Db, w.Patient.UserId), admin: true);
            var suspended = Assert.Single(forUserAsAdmin.Items);
            Assert.Equal("Account.Suspended", suspended.Action);
            Assert.Equal("Test Admin", suspended.ActorName);

            var forUserThemself = await Page(w, ActivityLogQueries.ForUser(w.Db, w.Patient.UserId), admin: false);
            Assert.Equal("Administrator", Assert.Single(forUserThemself.Items).ActorName);

            var hospitalLog = await Page(w, ActivityLogQueries.ForHospital(w.Db, w.Hospital.HospitalId));
            Assert.Contains(hospitalLog.Items, i => i.Action == "Hospital.Suspended" && i.ActorRole == "Admin");
        }

        [Fact]
        public async Task Sign_In_Is_Not_Logged_But_A_Doctors_First_Password_Change_Is()
        {
            var w = await SeedAsync();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "LifeLink_Super_Secret_Jwt_Signing_Key_2026_For_Development_Only_Must_Be_Long!", ["Jwt:Issuer"] = "LifeLinkAPI",
                ["Jwt:Audience"] = "LifeLinkApp", ["Jwt:ExpiryMinutes"] = "120"
            }).Build();
            var hasher = new PasswordHasherService();
            var auth = new AuthService(w.Db, hasher, new JwtService(config), new PasswordResetService(w.Db), new Mock<IEmailService>().Object);
            w.DoctorLogin.PasswordHash = hasher.HashPassword(w.DoctorLogin, "Temp@1234");
            w.Doctor.MustChangePassword = true;
            await w.Db.SaveChangesAsync();

            await auth.LoginAsync(new LoginRequestDto { Email = "doctor@example.test", Password = "Temp@1234" });
            Assert.Empty(w.Db.ActivityLogs); // sign-in is never recorded

            await auth.ChangePasswordAsync(w.DoctorLogin.UserId, new ChangePasswordRequestDto { CurrentPassword = "Temp@1234", NewPassword = "NewPass@1234" });
            var entry = Assert.Single(w.Db.ActivityLogs);
            Assert.Equal("Account.FirstPasswordChange", entry.Action);
            Assert.Equal(w.Hospital.HospitalId, entry.HospitalId); // also in the hospital's log
        }

        // ---------- Filters and paging ----------

        [Fact]
        public async Task User_Enrichment_Is_Limited_To_Their_Authorized_Request_And_Acceptance_Rows()
        {
            var w = await SeedAsync();
            var request = await new BloodRequestService(w.Db).CreateRequestAsync(w.Patient.UserId, Request(w, "A+"));
            var acceptance = new Acceptance
            {
                AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId,
                DonorUserId = w.Patient.UserId, Status = AcceptanceStatus.Accepted
            };
            w.Db.Acceptances.Add(acceptance);
            await ActivityLogger.AddAsync(w.Db, w.Patient.UserId, "Donation.Accepted", ActivityLogger.Types.Donation,
                acceptance.AcceptanceId, "Accepted a request.");

            var other = new User { UserId = Guid.NewGuid(), FirstName = "Other", LastName = "User", Email = "other@example.test" };
            w.Db.Users.Add(other);
            w.Db.UserRoles.Add(new UserRole { UserId = other.UserId, RoleId = 1 });
            await ActivityLogger.AddAsync(w.Db, other.UserId, "BloodRequest.Created", ActivityLogger.Types.BloodRequest,
                Guid.NewGuid(), "Other user's request.");
            await w.Db.SaveChangesAsync();

            var viewer = new ActivityLogViewerContext(w.Patient.UserId, new HashSet<string> { "User" }, null);
            var page = await ActivityLogQueries.PageAsync(w.Db, ActivityLogQueries.ForUser(w.Db, w.Patient.UserId),
                new ActivityLogQueryDto(), viewer);

            Assert.Equal(2, page.Total);
            var requestRow = page.Items.Single(i => i.Action == "BloodRequest.Created");
            Assert.Equal(request.BloodRequestId, requestRow.BloodRequestId);
            Assert.Equal("A+", requestRow.BloodGroup);
            Assert.Equal(w.Hospital.Name, requestRow.HospitalName);
            var acceptanceRow = page.Items.Single(i => i.Action == "Donation.Accepted");
            Assert.Equal(acceptance.AcceptanceId, acceptanceRow.AcceptanceId);
            Assert.Equal(request.BloodRequestId, acceptanceRow.BloodRequestId);
            Assert.DoesNotContain(page.Items, i => i.Summary == "Other user's request.");
        }

        [Fact]
        public async Task Complaint_And_Appeal_Expose_Only_Record_References()
        {
            var w = await SeedAsync();
            await ActivityLogger.AddAsync(w.Db, w.Patient.UserId, "Complaint.Filed", ActivityLogger.Types.Complaint,
                Guid.NewGuid(), "Complaint summary.");
            await ActivityLogger.AddAsync(w.Db, w.Patient.UserId, "Appeal.Submitted", ActivityLogger.Types.Appeal,
                Guid.NewGuid(), "Appeal summary.");
            await w.Db.SaveChangesAsync();

            var viewer = new ActivityLogViewerContext(w.Patient.UserId, new HashSet<string> { "User" }, null);
            var page = await ActivityLogQueries.PageAsync(w.Db, ActivityLogQueries.ForUser(w.Db, w.Patient.UserId),
                new ActivityLogQueryDto(), viewer);

            Assert.All(page.Items, item =>
            {
                Assert.NotNull(item.RecordReference);
                Assert.Null(item.BloodRequestId);
                Assert.Null(item.AcceptanceId);
                Assert.Null(item.HospitalName);
                Assert.Null(item.BloodGroup);
            });
        }

        [Fact]
        public async Task Hospital_Transfer_And_Packet_Enrichment_Requires_Hospital_Participation()
        {
            var w = await SeedAsync();
            var otherHospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Hospital B", Email = "b@example.test", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved };
            var transfer = new HospitalTransferRequest
            {
                TransferRequestId = Guid.NewGuid(), SenderHospitalId = w.Hospital.HospitalId,
                ReceiverHospitalId = otherHospital.HospitalId, BloodGroup = "O-", UnitsRequested = 1,
                Status = "Pending", TransferType = TransferTypes.Request
            };
            var packet = new BloodPacket
            {
                PacketId = Guid.NewGuid(), TrackingNumber = "PKT-00009999", HospitalId = w.Hospital.HospitalId,
                CreatedByHospitalId = w.Hospital.HospitalId, BloodGroup = "O-", CollectionDate = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(20), Status = BloodPacketStatus.Available, Source = BloodPacketSource.Manual
            };
            w.Db.Hospitals.Add(otherHospital);
            w.Db.HospitalTransferRequests.Add(transfer);
            w.Db.BloodPackets.Add(packet);
            await ActivityLogger.AddForHospitalAsync(w.Db, w.Hospital.HospitalId, "Transfer.RequestCreated",
                ActivityLogger.Types.Transfer, transfer.TransferRequestId, "Created transfer.");
            await ActivityLogger.AddForHospitalAsync(w.Db, w.Hospital.HospitalId, "Inventory.PacketEdited",
                ActivityLogger.Types.Inventory, packet.PacketId, "Edited packet.");
            await w.Db.SaveChangesAsync();

            var viewer = new ActivityLogViewerContext(w.Staff.UserId, new HashSet<string> { "HospitalStaff" }, w.Hospital.HospitalId);
            var page = await ActivityLogQueries.PageAsync(w.Db, ActivityLogQueries.ForHospital(w.Db, w.Hospital.HospitalId),
                new ActivityLogQueryDto(), viewer);
            var transferRow = page.Items.Single(i => i.Action == "Transfer.RequestCreated");
            Assert.Equal(w.Hospital.Name, transferRow.TransferSourceHospitalName);
            Assert.Equal(otherHospital.Name, transferRow.TransferDestinationHospitalName);
            Assert.Equal("PKT-00009999", page.Items.Single(i => i.Action == "Inventory.PacketEdited").PacketTrackingNumber);

            var unrelatedViewer = new ActivityLogViewerContext(w.Staff.UserId, new HashSet<string> { "HospitalStaff" }, Guid.NewGuid());
            await ActivityLogEnrichment.EnrichAsync(w.Db, new[] { new ActivityLogEntryDto { Action = "Transfer.RequestCreated", EntityId = transfer.TransferRequestId } }, unrelatedViewer);
            var unrelatedPacket = new ActivityLogEntryDto { Action = "Inventory.PacketEdited", EntityId = packet.PacketId };
            await ActivityLogEnrichment.EnrichAsync(w.Db, new[] { unrelatedPacket }, unrelatedViewer);
            Assert.Null(unrelatedPacket.PacketTrackingNumber);
        }

        [Fact]
        public async Task Doctor_Scope_Is_Not_Expanded_And_Unknown_Or_Missing_Entities_Degrade_To_Null()
        {
            var w = await SeedAsync();
            await ActivityLogger.AddForHospitalAsync(w.Db, w.Hospital.HospitalId, "Inventory.PacketsAdded",
                ActivityLogger.Types.Inventory, null, "Hospital-only activity.");
            await ActivityLogger.AddAsync(w.Db, w.DoctorLogin.UserId, "Future.Unknown", ActivityLogger.Types.Account,
                Guid.NewGuid(), "Unknown action.");
            await ActivityLogger.AddAsync(w.Db, w.DoctorLogin.UserId, "BloodRequest.Created", ActivityLogger.Types.BloodRequest,
                Guid.NewGuid(), "Missing request.");
            await w.Db.SaveChangesAsync();

            var viewer = new ActivityLogViewerContext(w.DoctorLogin.UserId, new HashSet<string> { "Doctor" }, w.Hospital.HospitalId);
            var page = await ActivityLogQueries.PageAsync(w.Db, ActivityLogQueries.ForUser(w.Db, w.DoctorLogin.UserId),
                new ActivityLogQueryDto(), viewer);
            Assert.Equal(2, page.Total);
            Assert.DoesNotContain(page.Items, i => i.Action == "Inventory.PacketsAdded");
            Assert.All(page.Items, i => Assert.Null(i.RecordReference));
        }

        [Fact]
        public async Task Filter_By_Type_And_Sri_Lanka_Date_Range_With_Paging()
        {
            var w = await SeedAsync();
            var day = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);
            // Sri Lanka is UTC+05:30: 18:00 UTC on 2 Oct = 23:30 on 2 Oct (out); 19:00 UTC on 2 Oct = 00:30 on 3 Oct (in);
            // 18:00 UTC on 3 Oct = 23:30 on 3 Oct (in); 18:36 UTC on 3 Oct = 00:06 on 4 Oct (out)
            var times = new[] { day.AddHours(-6), day.AddHours(-5), day.AddHours(18), day.AddHours(18.6) };
            foreach (var (t, i) in times.Select((t, i) => (t, i)))
            {
                w.Db.ActivityLogs.Add(new ActivityLog { ActorUserId = w.Patient.UserId, ActorRole = "User", ActorName = "Test Patient", OccurredAt = t,
                    Action = i % 2 == 0 ? "BloodRequest.Created" : "Complaint.Filed", EntityType = i % 2 == 0 ? "BloodRequest" : "Complaint", Summary = $"entry {i}" });
            }
            await w.Db.SaveChangesAsync();
            var scope = ActivityLogQueries.ForUser(w.Db, w.Patient.UserId);

            Assert.Equal(4, (await Page(w, scope)).Total);
            Assert.Equal(2, (await Page(w, scope, new ActivityLogQueryDto { Type = "Complaint" })).Total);

            // 3 Oct (Sri Lanka) covers entries 1 and 2 only
            var oneDay = await Page(w, scope, new ActivityLogQueryDto { From = new DateTime(2026, 10, 3), To = new DateTime(2026, 10, 3) });
            Assert.Equal(new[] { "entry 2", "entry 1" }, oneDay.Items.Select(i => i.Summary));

            var page2 = await Page(w, scope, new ActivityLogQueryDto { Page = 2, PageSize = 3 });
            Assert.Equal(4, page2.Total);
            Assert.Equal("entry 0", Assert.Single(page2.Items).Summary); // newest first
            Assert.Equal(ActivityLogger.RecordedFrom, page2.RecordedFrom);
            Assert.Contains("BloodRequest", page2.Types);
        }

        // ---------- Badges ----------

        [Fact]
        public async Task New_Requests_And_Transfers_Clear_When_Seen_And_Pending_Counts_Follow_The_Queues()
        {
            var w = await SeedAsync();
            var admin = Admin(w);
            await new BloodRequestService(w.Db).CreateRequestAsync(w.Patient.UserId, Request(w, "A+"));
            await new BloodRequestService(w.Db).CreateRequestAsync(w.Patient.UserId, Request(w, "B+"));
            w.Db.HospitalTransferRequests.Add(new HospitalTransferRequest { TransferRequestId = Guid.NewGuid(), SenderHospitalId = w.Hospital.HospitalId, ReceiverHospitalId = w.Hospital.HospitalId, BloodGroup = "A+", UnitsRequested = 1, TransferType = "Request", Status = "Pending" });
            w.Db.Hospitals.Add(new Hospital { HospitalId = Guid.NewGuid(), Name = "Waiting Hospital", Email = "waiting@example.test", ApprovalStatus = ApprovalStatus.Pending });
            var appeal = new Appeal { AppealId = Guid.NewGuid(), UserId = w.Patient.UserId, Reason = "Please", Status = AppealStatus.PENDING };
            w.Db.Appeals.Add(appeal);
            w.Db.AppealMessages.Add(new AppealMessage { MessageId = Guid.NewGuid(), AppealId = appeal.AppealId, Message = "Please", CreatedAt = DateTime.UtcNow });
            var complaint = new Complaint { ComplaintId = Guid.NewGuid(), UserId = w.Patient.UserId, ComplaintType = "Other", Subject = "S", Description = "D" };
            var deletedComplaint = new Complaint { ComplaintId = Guid.NewGuid(), UserId = w.Patient.UserId, ComplaintType = "Other", Subject = "S2", Description = "D", DeletedAt = DateTime.UtcNow };
            w.Db.Complaints.AddRange(complaint, deletedComplaint);
            await w.Db.SaveChangesAsync();

            var before = await admin.GetAttentionCountsAsync(w.Admin.UserId);
            Assert.Equal(2, before.NewBloodRequests);
            Assert.Equal(1, before.NewTransfers);
            Assert.Null(before.BloodRequestsSeenAt);
            Assert.Equal(1, before.PendingRegistrations);
            Assert.Equal(1, before.PendingAppeals);
            Assert.Equal(1, before.PendingComplaints); // the deleted complaint is not counted

            await admin.MarkAreaSeenAsync(w.Admin.UserId, AdminService.AttentionAreas.BloodRequests);
            var after = await admin.GetAttentionCountsAsync(w.Admin.UserId);
            Assert.Equal(0, after.NewBloodRequests);
            Assert.NotNull(after.BloodRequestsSeenAt);
            Assert.Equal(1, after.NewTransfers); // other tab not opened yet

            // The admin replies to the appeal: no longer pending
            w.Db.AppealMessages.Add(new AppealMessage { MessageId = Guid.NewGuid(), AppealId = appeal.AppealId, AdminId = w.Admin.UserId, Message = "Looking", CreatedAt = DateTime.UtcNow.AddSeconds(1) });
            await w.Db.SaveChangesAsync();
            Assert.Equal(0, (await admin.GetAttentionCountsAsync(w.Admin.UserId)).PendingAppeals);

            // A new request after "seen" is new again; another admin has their own markers
            await new BloodRequestService(w.Db).CreateRequestAsync(w.Patient.UserId, Request(w, "O-"));
            Assert.Equal(1, (await admin.GetAttentionCountsAsync(w.Admin.UserId)).NewBloodRequests);
            await Assert.ThrowsAsync<InvalidOperationException>(() => admin.MarkAreaSeenAsync(w.Admin.UserId, "unknown"));
        }

        [Fact]
        public async Task The_Admin_Request_List_Includes_Deleted_Requests()
        {
            var w = await SeedAsync();
            var service = new BloodRequestService(w.Db);
            var kept = await service.CreateRequestAsync(w.Patient.UserId, Request(w, "A+"));
            var deleted = await service.CreateRequestAsync(w.Patient.UserId, Request(w, "B+"));
            await service.DeleteRequestAsync(deleted.BloodRequestId, w.Patient.UserId);

            var all = (await service.GetAllRequestsForAdminAsync()).ToList();
            Assert.Equal(2, all.Count);
            var row = all.Single(r => r.BloodRequestId == deleted.BloodRequestId);
            Assert.Equal("Deleted", row.Status);
            Assert.NotNull(row.DeletedAt);
            Assert.Single(await service.GetMyRequestsAsync(w.Patient.UserId)); // the creator sees only the kept one
            Assert.Contains(w.Db.ActivityLogs, a => a.Action == "BloodRequest.Deleted" && a.EntityId == deleted.BloodRequestId);
            Assert.Contains(all, r => r.BloodRequestId == kept.BloodRequestId);
        }
    }
}
