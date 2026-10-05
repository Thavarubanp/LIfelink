using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.DTOs.Admin;
using LifeLink.DTOs.BloodRequests;
using LifeLink.DTOs.Complaints;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using LifeLink.Services.Admin;
using LifeLink.Services.BloodCompatibility;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Common;
using LifeLink.Services.Complaints;
using LifeLink.Services.Notification;
using LifeLink.Services.Transfer;
using LifeLink.Services.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Phase 3B: admin Suspend / Lift of blood requests and transfers (owner's Q7 exceptions: donor withdraw and creator
    /// delete), one-way "Message from Administrator", and Change A (a complaint without a target is a question to the admin).
    /// </summary>
    public class AdminSuspensionAndMessagesTests
    {
        private sealed class World
        {
            public string DbName = null!;
            public AppDbContext Db = null!;
            public Hospital Hospital = null!;
            public Hospital Other = null!;
            public User Admin = null!;
            public User Patient = null!;
            public User Donor = null!;
            public User Staff = null!;
            public User DoctorLogin = null!;
            public Doctor Doctor = null!;
            public AdminOversightActionsService Actions = null!;
            public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(DbName).Options);
        }

        private static async Task<World> SeedAsync()
        {
            var name = Guid.NewGuid().ToString();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);
            db.Database.EnsureCreated(); // roles: 1 User, 2 HospitalStaff, 3 Doctor, 4 Admin
            var w = new World
            {
                DbName = name,
                Db = db,
                Hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Test Hospital A", Email = "hospital.a@example.test", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved },
                Other = new Hospital { HospitalId = Guid.NewGuid(), Name = "Test Hospital B", Email = "hospital.b@example.test", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved },
                Admin = new User { UserId = Guid.NewGuid(), FirstName = "Test", LastName = "Admin", Email = "admin@example.test" },
                Patient = new User { UserId = Guid.NewGuid(), FirstName = "Test", LastName = "Patient", Email = "patient@example.test" },
                Donor = new User { UserId = Guid.NewGuid(), FirstName = "Test", LastName = "Donor", Email = "donor@example.test", BloodGroup = "B+" },
                Staff = new User { UserId = Guid.NewGuid(), FirstName = "Hospital", LastName = "Staff", Email = "hospital.a@example.test" },
                DoctorLogin = new User { UserId = Guid.NewGuid(), FirstName = "Test", LastName = "Doctor", Email = "doctor@example.test" }
            };
            w.Doctor = new Doctor { DoctorId = Guid.NewGuid(), HospitalId = w.Hospital.HospitalId, UserId = w.DoctorLogin.UserId, FirstName = "Test", LastName = "Doctor", Email = "doctor@example.test", LicenseNumber = "SLMC/T1", IsActive = true, MustChangePassword = false };
            db.Hospitals.AddRange(w.Hospital, w.Other);
            db.Users.AddRange(w.Admin, w.Patient, w.Donor, w.Staff, w.DoctorLogin);
            db.UserRoles.AddRange(
                new UserRole { UserId = w.Admin.UserId, RoleId = 4 }, new UserRole { UserId = w.Patient.UserId, RoleId = 1 },
                new UserRole { UserId = w.Donor.UserId, RoleId = 1 }, new UserRole { UserId = w.Staff.UserId, RoleId = 2 },
                new UserRole { UserId = w.DoctorLogin.UserId, RoleId = 3 });
            db.Doctors.Add(w.Doctor);
            await db.SaveChangesAsync();
            w.Actions = new AdminOversightActionsService(db);
            return w;
        }

        private static async Task<BloodRequest> AddRequestAsync(World w, BloodRequestStatus status, bool assignDoctor = true, DateTime? expiry = null)
        {
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(), PatientUserId = w.Patient.UserId, HospitalId = w.Hospital.HospitalId, BloodGroup = "B+",
                UnitsRequired = 2, Reason = "Surgery", Priority = "High", Status = status, ExpiryDate = expiry ?? DateTime.UtcNow.AddDays(3)
            };
            w.Db.BloodRequests.Add(request);
            if (assignDoctor)
            {
                w.Db.BloodRequestVerifications.Add(new BloodRequestVerification { BloodRequestId = request.BloodRequestId, DoctorId = w.Doctor.DoctorId });
            }
            await w.Db.SaveChangesAsync();
            return request;
        }

        private static async Task<Acceptance> AddAcceptanceAsync(World w, BloodRequest request, AcceptanceStatus status)
        {
            var acceptance = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = w.Donor.UserId, Status = status };
            w.Db.Acceptances.Add(acceptance);
            await w.Db.SaveChangesAsync();
            return acceptance;
        }

        private static AcceptanceService Acceptances(World w) => new(w.Db, new BloodCompatibilityService());
        private static VerificationService Verification(World w) => new(w.Db, new Mock<INotificationAgentService>().Object);

        private static async Task<HospitalTransferRequest> AddTransferAsync(World w)
        {
            var transfer = new HospitalTransferRequest
            {
                TransferRequestId = Guid.NewGuid(), SenderHospitalId = w.Other.HospitalId, ReceiverHospitalId = w.Hospital.HospitalId, BloodGroup = "A+",
                UnitsRequested = 1, Status = TransferRequestStatus.Pending.ToString(), TransferType = TransferTypes.Request, Notes = "3B test"
            };
            w.Db.HospitalTransferRequests.Add(transfer);
            await w.Db.SaveChangesAsync();
            return transfer;
        }

        // ---------- Suspend / lift a blood request ----------

        [Fact]
        public async Task Suspend_Needs_A_Reason_And_An_Open_Request()
        {
            var w = await SeedAsync();
            var open = await AddRequestAsync(w, BloodRequestStatus.Approved);
            var done = await AddRequestAsync(w, BloodRequestStatus.Completed);

            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Actions.SuspendRequestAsync(open.BloodRequestId, w.Admin.UserId, "  "));
            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Actions.SuspendRequestAsync(done.BloodRequestId, w.Admin.UserId, "Check"));

            await w.Actions.SuspendRequestAsync(open.BloodRequestId, w.Admin.UserId, "Possible duplicate");
            await Assert.ThrowsAsync<ConflictException>(() => w.Actions.SuspendRequestAsync(open.BloodRequestId, w.Admin.UserId, "Again"));
            var saved = await w.Db.BloodRequests.AsNoTracking().SingleAsync(r => r.BloodRequestId == open.BloodRequestId);
            Assert.NotNull(saved.AdminSuspendedAt);
            Assert.Equal("Possible duplicate", saved.AdminSuspensionReason);
            Assert.Equal(BloodRequestStatus.Approved, saved.Status); // the admin never changes the status
        }

        [Fact]
        public async Task Suspend_And_Lift_Notify_Creator_Hospital_Doctor_And_Donors_And_Are_Logged()
        {
            var w = await SeedAsync();
            var request = await AddRequestAsync(w, BloodRequestStatus.Approved);
            await AddAcceptanceAsync(w, request, AcceptanceStatus.Accepted);

            await w.Actions.SuspendRequestAsync(request.BloodRequestId, w.Admin.UserId, "Under review");
            var notes = await w.Db.Notifications.Where(n => n.NotificationType == "BloodRequestSuspended").ToListAsync();
            Assert.Contains(notes, n => n.UserId == w.Patient.UserId);
            Assert.Contains(notes, n => n.HospitalId == w.Hospital.HospitalId);
            Assert.Contains(notes, n => n.UserId == w.DoctorLogin.UserId);
            Assert.Contains(notes, n => n.UserId == w.Donor.UserId && n.Message.Contains("withdraw"));
            Assert.All(notes, n => Assert.Contains("Under review", n.Message));

            await w.Actions.LiftRequestAsync(request.BloodRequestId, w.Admin.UserId);
            Assert.Equal(4, await w.Db.Notifications.CountAsync(n => n.NotificationType == "BloodRequestSuspensionLifted"));
            await Assert.ThrowsAsync<ConflictException>(() => w.Actions.LiftRequestAsync(request.BloodRequestId, w.Admin.UserId));

            var log = await w.Db.ActivityLogs.Where(l => l.EntityId == request.BloodRequestId).Select(l => l.Action).ToListAsync();
            Assert.Contains("BloodRequest.Suspended", log);
            Assert.Contains("BloodRequest.SuspensionLifted", log);
        }

        [Fact]
        public async Task While_Suspended_Every_Action_Is_Refused_With_409()
        {
            var w = await SeedAsync();
            var pending = await AddRequestAsync(w, BloodRequestStatus.Pending, assignDoctor: false);
            var verified = await AddRequestAsync(w, BloodRequestStatus.Verified);
            var approved = await AddRequestAsync(w, BloodRequestStatus.Approved);
            var screened = await AddAcceptanceAsync(w, approved, AcceptanceStatus.ScreeningPending);
            foreach (var r in new[] { pending, verified, approved })
            {
                await w.Actions.SuspendRequestAsync(r.BloodRequestId, w.Admin.UserId, "Review");
            }

            var requests = new BloodRequestService(w.Db);
            await Assert.ThrowsAsync<ConflictException>(() => requests.UpdatePendingRequestAsync(pending.BloodRequestId, w.Patient.UserId, new UpdateBloodRequestDto { BloodGroup = "B+", UnitsRequired = 1 }));
            await Assert.ThrowsAsync<ConflictException>(() => requests.CancelRequestAsync(approved.BloodRequestId, w.Patient.UserId));
            await Assert.ThrowsAsync<ConflictException>(() => Verification(w).VerifyBloodRequestAsync(pending.BloodRequestId, w.Hospital.HospitalId, w.Doctor.DoctorId));
            await Assert.ThrowsAsync<ConflictException>(() => Verification(w).RejectBloodRequestByHospitalAsync(pending.BloodRequestId, w.Hospital.HospitalId, "No"));
            await Assert.ThrowsAsync<ConflictException>(() => Verification(w).ApproveBloodRequestAsync(verified.BloodRequestId, w.DoctorLogin.UserId, null));
            await Assert.ThrowsAsync<ConflictException>(() => Verification(w).RejectBloodRequestAsync(verified.BloodRequestId, w.DoctorLogin.UserId, "No"));
            await Assert.ThrowsAsync<ConflictException>(() => Acceptances(w).AcceptRequestAsync(w.Patient.UserId, new CreateAcceptanceDto { BloodRequestId = approved.BloodRequestId }));
            await Assert.ThrowsAsync<ConflictException>(() => Acceptances(w).FinalizeDonorSelectionAsync(approved.BloodRequestId, new List<Guid> { screened.AcceptanceId }, w.Staff.UserId, w.Hospital.HospitalId));
            // The screening agent's callbacks (interview opened, report submitted) are refused too
            await Assert.ThrowsAsync<ConflictException>(() => Acceptances(w).SubmitScreeningReportAsync(new ScreeningReportNotificationDto
            {
                AcceptanceId = screened.AcceptanceId.ToString(), ReportJson = "{}", Summary = "s", RiskLevel = "LOW", Recommendation = "Eligible", Status = "SubmittedToDoctor"
            }));
            var forAgent = await Acceptances(w).GetAcceptanceByIdAsync(screened.AcceptanceId);
            Assert.True(forAgent!.RequestSuspended);
        }

        [Fact]
        public async Task While_Suspended_A_Donor_Can_Withdraw_And_The_Creator_Can_Delete_And_The_Admin_Is_Told()
        {
            var w = await SeedAsync();
            var request = await AddRequestAsync(w, BloodRequestStatus.Approved);
            var acceptance = await AddAcceptanceAsync(w, request, AcceptanceStatus.Accepted);
            await w.Actions.SuspendRequestAsync(request.BloodRequestId, w.Admin.UserId, "Review");

            var withdrawn = await Acceptances(w).CancelAcceptanceAsync(acceptance.AcceptanceId, w.Donor.UserId);
            Assert.Equal("Cancelled", withdrawn.Status);

            await new BloodRequestService(w.Db).DeleteRequestAsync(request.BloodRequestId, w.Patient.UserId);
            var saved = await w.Db.BloodRequests.AsNoTracking().SingleAsync(r => r.BloodRequestId == request.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Deleted, saved.Status);
            Assert.NotNull(saved.AdminSuspendedAt); // the admin sees Deleted + Suspended
            Assert.True(await w.Db.Notifications.AnyAsync(n => n.UserId == w.Admin.UserId && n.NotificationType == "SuspendedRequestDeleted"));
            Assert.True(await w.Db.ActivityLogs.AnyAsync(l => l.Action == "BloodRequest.Deleted" && l.Summary.Contains("suspended by the administrator")));
            var adminRow = (await new BloodRequestService(w.Db).GetAllRequestsForAdminAsync()).Single(r => r.BloodRequestId == request.BloodRequestId);
            Assert.True(adminRow.IsSuspended);
            Assert.Equal("Deleted", adminRow.Status);
        }

        [Fact]
        public async Task Suspended_Requests_Are_Hidden_And_Legacy_Expiry_Never_Closes_Them()
        {
            var w = await SeedAsync();
            var overdue = await AddRequestAsync(w, BloodRequestStatus.Approved, expiry: DateTime.UtcNow.AddDays(3));
            await w.Actions.SuspendRequestAsync(overdue.BloodRequestId, w.Admin.UserId, "Review");
            var tracked = await w.Db.BloodRequests.SingleAsync(r => r.BloodRequestId == overdue.BloodRequestId);
            tracked.ExpiryDate = DateTime.UtcNow.AddMinutes(-5);
            await w.Db.SaveChangesAsync();

            Assert.DoesNotContain(await new BloodRequestService(w.Db).GetPublicRequestsAsync(), r => r.BloodRequestId == overdue.BloodRequestId);
            var sweep = new RequestExpiryService(w.Db, NullLogger<RequestExpiryService>.Instance);
            Assert.Equal(0, await sweep.ProcessExpiredRequestsAsync());

            await w.Actions.LiftRequestAsync(overdue.BloodRequestId, w.Admin.UserId);
            Assert.Equal(0, await sweep.ProcessExpiredRequestsAsync());
            Assert.Contains(await new BloodRequestService(w.Db).GetPublicRequestsAsync(), r => r.BloodRequestId == overdue.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Approved, tracked.Status);
        }

        [Fact]
        public async Task A_Suspend_Racing_An_Edit_Makes_One_Of_Them_Fail()
        {
            var w = await SeedAsync();
            var request = await AddRequestAsync(w, BloodRequestStatus.Pending, assignDoctor: false);

            // The creator's edit reads the request before the admin's suspend is saved
            await using var creatorContext = w.NewContext();
            var stale = await creatorContext.BloodRequests.SingleAsync(r => r.BloodRequestId == request.BloodRequestId);
            Assert.Null(stale.AdminSuspendedAt);

            await using var adminContext = w.NewContext();
            await new AdminOversightActionsService(adminContext).SuspendRequestAsync(request.BloodRequestId, w.Admin.UserId, "Review");

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                new BloodRequestService(creatorContext).UpdatePendingRequestAsync(request.BloodRequestId, w.Patient.UserId, new UpdateBloodRequestDto { BloodGroup = "B+", UnitsRequired = 1 }));
        }

        // ---------- Transfers ----------

        [Fact]
        public async Task A_Suspended_Transfer_Cannot_Be_Accepted_Rejected_Or_Withdrawn_Until_Lifted()
        {
            var w = await SeedAsync();
            var transfer = await AddTransferAsync(w);
            await w.Actions.SuspendTransferAsync(transfer.TransferRequestId, w.Admin.UserId, "Check stock records");
            Assert.Equal(2, await w.Db.Notifications.CountAsync(n => n.NotificationType == "TransferSuspended"));
            Assert.Equal(2, await w.Db.ActivityLogs.CountAsync(l => l.Action == "Transfer.Suspended")); // one in each hospital's log

            var service = new TransferRequestService(w.Db);
            await Assert.ThrowsAsync<ConflictException>(() => service.ApproveTransferRequestAsync(transfer.TransferRequestId, w.Other.HospitalId));
            await Assert.ThrowsAsync<ConflictException>(() => service.RejectTransferRequestAsync(transfer.TransferRequestId, w.Other.HospitalId, "No"));
            await Assert.ThrowsAsync<ConflictException>(() => service.DeleteTransferRequestAsync(transfer.TransferRequestId, w.Hospital.HospitalId));
            Assert.True((await service.GetTransferRequestAsync(transfer.TransferRequestId))!.IsSuspended);

            await w.Actions.LiftTransferAsync(transfer.TransferRequestId, w.Admin.UserId);
            var deleted = await service.DeleteTransferRequestAsync(transfer.TransferRequestId, w.Hospital.HospitalId);
            Assert.Equal("Cancelled", deleted.Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Actions.SuspendTransferAsync(transfer.TransferRequestId, w.Admin.UserId, "Too late"));
        }

        // ---------- Messages from the Administrator ----------

        [Fact]
        public async Task Messages_Go_To_One_User_Or_One_Hospital_But_Never_To_Doctors_Or_Admins()
        {
            var w = await SeedAsync();
            await w.Actions.SendMessageAsync(w.Admin.UserId, new AdminMessageDto { UserId = w.Donor.UserId, Subject = "Donation drive", Message = "Thank you for donating." });
            await w.Actions.SendMessageAsync(w.Admin.UserId, new AdminMessageDto { HospitalId = w.Hospital.HospitalId, Subject = "Stock audit", Message = "Please review your records." });

            var toDonor = await w.Db.Notifications.SingleAsync(n => n.UserId == w.Donor.UserId);
            Assert.Equal("AdminMessage", toDonor.NotificationType);
            Assert.Equal("Message from Administrator: Donation drive", toDonor.Title);
            Assert.True(await w.Db.Notifications.AnyAsync(n => n.HospitalId == w.Hospital.HospitalId && n.NotificationType == "AdminMessage"));
            Assert.Equal(2, await w.Db.ActivityLogs.CountAsync(l => l.Action == "Admin.MessageSent"));
            Assert.True(await w.Db.ActivityLogs.AnyAsync(l => l.Action == "Admin.MessageSent" && l.SubjectUserId == w.Donor.UserId));

            foreach (var refused in new[]
                     {
                         new AdminMessageDto { UserId = w.DoctorLogin.UserId, Subject = "Hello", Message = "Doctors are reached via their hospital." },
                         new AdminMessageDto { UserId = w.Admin.UserId, Subject = "Hello", Message = "Admins cannot be messaged." },
                         new AdminMessageDto { UserId = w.Staff.UserId, Subject = "Hello", Message = "Use the hospital profile." },
                         new AdminMessageDto { UserId = w.Donor.UserId, HospitalId = w.Hospital.HospitalId, Subject = "Hello", Message = "Two recipients." },
                         new AdminMessageDto { Subject = "Hello", Message = "No recipient at all." },
                         new AdminMessageDto { UserId = w.Donor.UserId, Subject = "Hi", Message = "Subject too short." }
                     })
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => w.Actions.SendMessageAsync(w.Admin.UserId, refused));
            }
            Assert.Equal(2, await w.Db.Notifications.CountAsync(n => n.NotificationType == "AdminMessage"));
        }

        // ---------- Change A: complaints without a target ----------

        [Fact]
        public async Task A_Complaint_Without_A_Target_Is_A_General_Question_And_Targets_Keep_Their_Rules()
        {
            var w = await SeedAsync();
            var service = new ComplaintService(w.Db, new AdminNotificationService(w.Db, new Mock<LifeLink.Services.Auth.IEmailService>().Object, NullLogger<AdminNotificationService>.Instance));

            var question = await service.CreateComplaintAsync(w.Donor.UserId, null, new CreateComplaintDto
            {
                ComplaintType = "Other", Subject = "How do I update my blood group?", Description = "I want to know where to change it."
            });
            Assert.Null(question.HospitalId);
            Assert.Null(question.TargetUserId);
            Assert.True(question.AwaitingAdminReply); // same thread: the admin replies next

            // A given target still follows the existing rules (for example not yourself, not a doctor)
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateComplaintAsync(w.Donor.UserId, null, new CreateComplaintDto
            {
                ComplaintType = "Other", Subject = "About myself", Description = "Filing against myself.", TargetUserId = w.Donor.UserId
            }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateComplaintAsync(w.Donor.UserId, null, new CreateComplaintDto
            {
                ComplaintType = "Other", Subject = "About a doctor", Description = "Doctors are filed via the hospital.", TargetUserId = w.DoctorLogin.UserId
            }));
        }
    }
}
