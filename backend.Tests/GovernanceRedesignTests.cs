using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.DTOs.Admin;
using LifeLink.DTOs.Appeals;
using LifeLink.DTOs.Auth;
using LifeLink.DTOs.BloodRequests;
using LifeLink.DTOs.Complaints;
using LifeLink.DTOs.Transfer;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using LifeLink.Services.Admin;
using LifeLink.Services.Appeals;
using LifeLink.Services.Auth;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Complaints;
using LifeLink.Services.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Governance redesign: appeal threads, permanent block, self-delete, single-Admin transfer,
    /// admin self-protection, hospital suspension effects and complaint-target eligibility.
    /// </summary>
    public class GovernanceRedesignTests
    {
        private const int UserRole = 1, StaffRole = 2, DoctorRole = 3, AdminRole = 4; // seeded by AppDbContext.HasData

        private sealed class Seed
        {
            public AppDbContext Db = null!;
            public AdminNotificationService Notify = null!;
            public Guid Admin, Donor, Other, Staff, DoctorUser, HospitalId;
        }

        private static async Task<Seed> SeedAsync()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Database.EnsureCreated();
            var s = new Seed { Db = db, Admin = Guid.NewGuid(), Donor = Guid.NewGuid(), Other = Guid.NewGuid(), Staff = Guid.NewGuid(), DoctorUser = Guid.NewGuid(), HospitalId = Guid.NewGuid() };
            await db.Hospitals.AddAsync(new Hospital { HospitalId = s.HospitalId, Name = "Venus", Email = "venus@h.org", IsVerified = true });
            foreach (var (id, email, role) in new[] { (s.Admin, "admin@t.org", AdminRole), (s.Donor, "donor@t.org", UserRole), (s.Other, "other@t.org", UserRole), (s.Staff, "venus@h.org", StaffRole), (s.DoctorUser, "doc@t.org", DoctorRole) })
            {
                await db.Users.AddAsync(new User { UserId = id, FirstName = "F", LastName = "L", Email = email, PhoneNumber = "0711111111", Address = "Addr", Gender = "Male",
                    PasswordHash = new PasswordHasherService().HashPassword(new User(), "Passw0rd!") });
                await db.UserRoles.AddAsync(new UserRole { UserId = id, RoleId = role });
            }
            await db.Doctors.AddAsync(new Doctor { DoctorId = Guid.NewGuid(), UserId = s.DoctorUser, HospitalId = s.HospitalId, FirstName = "D", LastName = "R", Email = "doc@t.org", LicenseNumber = "SLMC/1", IsActive = true });
            await db.SaveChangesAsync();
            s.Notify = new AdminNotificationService(db, new Mock<IEmailService>().Object, NullLogger<AdminNotificationService>.Instance);
            return s;
        }

        private static AppealService Appeals(Seed s) => new(s.Db, s.Notify);
        private static AdminService AdminSvc(Seed s) => new(s.Db, s.Notify);
        private static ReviewComplaintDto Msg(string text, bool file = false) => new() { Notes = text, AttachmentUrl = file ? "data:text/plain;base64,SGk=" : null, AttachmentName = file ? "note.txt" : null };
        private static ReviewAppealDto Decision(string text) => new() { AdminResponse = text };

        private static AuthService Auth(Seed s)
        {
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "LifeLink_Super_Secret_Jwt_Signing_Key_2026_For_Development_Only_Must_Be_Long!", ["Jwt:Issuer"] = "LifeLinkAPI", ["Jwt:Audience"] = "LifeLinkApp", ["Jwt:ExpiryMinutes"] = "120"
            }).Build();
            return new AuthService(s.Db, new PasswordHasherService(), new JwtService(cfg), new PasswordResetService(s.Db), new Mock<IEmailService>().Object);
        }

        private static async Task SuspendDonorAsync(Seed s) => await AdminSvc(s).SuspendUserAsync(s.Donor, new SuspendUserDto { Reason = "Missed appointments" }, s.Admin);

        // ---------- Appeal threads ----------

        [Fact]
        public async Task Appeal_Thread_Alternates_Reject_Keeps_It_Open_Close_Makes_It_Read_Only()
        {
            var s = await SeedAsync();
            await SuspendDonorAsync(s);
            var svc = Appeals(s);

            var appeal = await svc.SubmitAppealAsync(new CreateAppealDto { Reason = "Please review my suspension.", AttachmentUrl = "data:text/plain;base64,SGk=", AttachmentName = "proof.txt" }, s.Donor);
            Assert.True(appeal.AwaitingAdminReply);
            Assert.Equal("proof.txt", appeal.Messages.Single().AttachmentName);
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AppellantReplyAsync(appeal.AppealId, s.Donor, Msg("Any news?")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SubmitAppealAsync(new CreateAppealDto { Reason = "Second appeal attempt." }, s.Donor)); // one open thread

            var afterAdmin = await svc.AdminReplyAsync(appeal.AppealId, s.Admin, Msg("Send the certificate.", file: true));
            Assert.True(afterAdmin.CanAppellantReply);
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AdminReplyAsync(appeal.AppealId, s.Admin, Msg("Again")));

            await svc.AppellantReplyAsync(appeal.AppealId, s.Donor, Msg("Here it is.", file: true));
            var rejected = await svc.RejectAppealAsync(appeal.AppealId, s.Admin, Decision("Not sufficient."));
            Assert.Equal("REJECTED", rejected.Status);
            Assert.False(rejected.IsClosed);

            var reopened = await svc.AppellantReplyAsync(appeal.AppealId, s.Donor, Msg("Please reconsider."));
            Assert.Equal("PENDING", reopened.Status); // user keeps replying after a rejection

            var closed = await svc.CloseAppealAsync(appeal.AppealId, s.Admin, Decision("Thread closed."));
            Assert.True(closed.IsClosed);
            Assert.False(closed.CanAppellantReply);
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AdminReplyAsync(appeal.AppealId, s.Admin, Msg("Late")));
            Assert.True((await s.Db.Users.FindAsync(s.Donor))!.IsSuspended); // closing does not lift the suspension

            // A new thread may be opened once the old one is closed
            Assert.NotNull(await svc.SubmitAppealAsync(new CreateAppealDto { Reason = "New appeal after closure." }, s.Donor));
        }

        [Fact]
        public async Task Doctors_Cannot_Appeal_Or_Reply_And_Staff_Appeal_For_Their_Suspended_Hospital()
        {
            var s = await SeedAsync();
            await AdminSvc(s).SuspendHospitalAsync(s.HospitalId, new SuspendHospitalDto { Reason = "Audit" });
            var svc = Appeals(s);

            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SubmitAppealAsync(new CreateAppealDto { Reason = "Doctor appeal attempt." }, s.DoctorUser));

            var staffAppeal = await svc.SubmitAppealAsync(new CreateAppealDto { Reason = "Hospital appeal from staff." }, s.Staff);
            Assert.Equal(s.HospitalId, staffAppeal.HospitalId);
            await svc.AdminReplyAsync(staffAppeal.AppealId, s.Admin, Msg("Provide the audit report."));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.AppellantReplyAsync(staffAppeal.AppealId, s.DoctorUser, Msg("Doctor reply")));
            Assert.Single(await svc.GetMyAppealsAsync(s.DoctorUser)); // doctors can still view their hospital's thread
            Assert.NotNull(await svc.AppellantReplyAsync(staffAppeal.AppealId, s.Staff, Msg("Report attached.")));

            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PermanentlyBlockAsync(staffAppeal.AppealId, s.Admin, Decision("Block hospital"))); // hospitals are never blocked
        }

        // ---------- Permanent block ----------

        [Fact]
        public async Task Permanent_Block_Anonymizes_Keeps_History_And_Excludes_The_Account_Everywhere()
        {
            var s = await SeedAsync();
            var request = new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = s.Donor, HospitalId = s.HospitalId, BloodGroup = "A+", UnitsRequired = 1, Status = BloodRequestStatus.Approved };
            await s.Db.BloodRequests.AddAsync(request);
            await s.Db.Notifications.AddAsync(new Notification { UserId = s.Donor, Title = "t", Message = "m" });
            await s.Db.SaveChangesAsync();

            var result = await AdminSvc(s).BlockUserAsync(s.Donor, s.Admin);

            Assert.True(result.IsPermanentlyBlocked);
            var user = (await s.Db.Users.FindAsync(s.Donor))!;
            Assert.Equal(AccountStatus.Blocked, user.AccountStatus);
            Assert.Equal("donor@t.org", user.Email);   // kept internally
            Assert.Equal("F", user.FirstName);          // name stays visible
            Assert.Equal(string.Empty, user.PhoneNumber);
            Assert.Equal(string.Empty, user.Address);
            Assert.False(await s.Db.UserRoles.AnyAsync(ur => ur.UserId == s.Donor));
            Assert.False(await s.Db.Notifications.AnyAsync(n => n.UserId == s.Donor));
            Assert.NotNull(await s.Db.BloodRequests.FindAsync(request.BloodRequestId)); // history preserved

            var auth = Auth(s);
            var login = await Assert.ThrowsAsync<InvalidOperationException>(() => auth.LoginAsync(new LoginRequestDto { Email = "donor@t.org", Password = "Passw0rd!" }));
            Assert.Contains("permanently blocked", login.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => auth.RegisterAsync(new RegisterRequestDto { FirstName = "N", LastName = "U", Email = "donor@t.org", Password = "Passw0rd!" }));

            await Assert.ThrowsAsync<InvalidOperationException>(() => new BloodRequestService(s.Db).CreateRequestAsync(s.Donor,
                new CreateBloodRequestDto { HospitalId = s.HospitalId, BloodGroup = "B+", UnitsRequired = 1, Reason = "Surgery", Priority = "High" }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new AcceptanceService(s.Db, null!).AcceptRequestAsync(s.Donor, new CreateAcceptanceDto { BloodRequestId = request.BloodRequestId, DonorBloodGroup = "A+" }));
            var complaint = await Assert.ThrowsAsync<InvalidOperationException>(() => new ComplaintService(s.Db, s.Notify).CreateComplaintAsync(s.Other, null,
                new CreateComplaintDto { ComplaintType = "Other", Subject = "About a donor", Description = "Complaint description text.", TargetUserId = s.Donor }));
            Assert.Contains("Permanently blocked", complaint.Message);
        }

        [Theory]
        [InlineData("Staff")]
        [InlineData("DoctorUser")]
        [InlineData("Admin")]
        public async Task Only_Donor_Patient_Accounts_Can_Be_Permanently_Blocked(string who)
        {
            var s = await SeedAsync();
            var id = who switch { "Staff" => s.Staff, "DoctorUser" => s.DoctorUser, _ => s.Admin };
            await Assert.ThrowsAsync<InvalidOperationException>(() => AdminSvc(s).BlockUserAsync(id, s.Admin));
            Assert.NotEqual(AccountStatus.Blocked, (await s.Db.Users.FindAsync(id))!.AccountStatus);
        }

        // ---------- Self-delete ----------

        [Fact]
        public async Task Self_Delete_Anonymizes_Keeps_History_And_Frees_The_Email()
        {
            var s = await SeedAsync();
            var request = new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = s.Donor, HospitalId = s.HospitalId, BloodGroup = "A+", UnitsRequired = 1, Status = BloodRequestStatus.Completed };
            await s.Db.BloodRequests.AddAsync(request);
            await s.Db.SaveChangesAsync();
            var auth = Auth(s);

            await auth.DeleteMyAccountAsync(s.Donor);

            var user = (await s.Db.Users.FindAsync(s.Donor))!;
            Assert.Equal(AccountStatus.Deleted, user.AccountStatus);
            Assert.Equal("Deleted User", $"{user.FirstName} {user.LastName}");
            Assert.NotEqual("donor@t.org", user.Email);
            Assert.NotNull(await s.Db.BloodRequests.FindAsync(request.BloodRequestId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new ComplaintService(s.Db, s.Notify).CreateComplaintAsync(s.Other, null,
                new CreateComplaintDto { ComplaintType = "Other", Subject = "About a donor", Description = "Complaint description text.", TargetUserId = s.Donor }));

            // The same email can register again
            var again = await auth.RegisterAsync(new RegisterRequestDto { FirstName = "N", LastName = "U", Email = "donor@t.org", Password = "Passw0rd!" });
            Assert.NotEqual(s.Donor, again.UserId);

            // Only donor/patient accounts can self-delete
            await Assert.ThrowsAsync<InvalidOperationException>(() => auth.DeleteMyAccountAsync(s.Staff));
            await Assert.ThrowsAsync<InvalidOperationException>(() => auth.DeleteMyAccountAsync(s.Admin));
        }

        // ---------- Single Admin ----------

        [Fact]
        public async Task Promotion_Swaps_Admin_Ownership_And_Notifies_The_New_Admin()
        {
            var s = await SeedAsync();
            await AdminSvc(s).PromoteToAdminAsync(s.Donor, s.Admin);

            var adminHolders = await s.Db.UserRoles.Where(ur => ur.RoleId == AdminRole).Select(ur => ur.UserId).ToListAsync();
            Assert.Equal(new[] { s.Donor }, adminHolders);
            Assert.True(await s.Db.UserRoles.AnyAsync(ur => ur.UserId == s.Admin && ur.RoleId == UserRole));
            Assert.False(await s.Db.UserRoles.AnyAsync(ur => ur.UserId == s.Donor && ur.RoleId == UserRole));
            Assert.True(await s.Db.Notifications.AnyAsync(n => n.UserId == s.Donor && n.NotificationType == "AdminTransfer"));
            Assert.Equal("F", (await s.Db.Users.FindAsync(s.Admin))!.FirstName); // profile preserved
        }

        [Theory]
        [InlineData("self")]
        [InlineData("staff")]
        [InlineData("doctor")]
        [InlineData("suspended")]
        [InlineData("blocked")]
        public async Task Ineligible_Accounts_Cannot_Be_Promoted(string target)
        {
            var s = await SeedAsync();
            var admin = AdminSvc(s);
            if (target == "suspended") await SuspendDonorAsync(s);
            if (target == "blocked") await admin.BlockUserAsync(s.Donor, s.Admin);
            var id = target switch { "self" => s.Admin, "staff" => s.Staff, "doctor" => s.DoctorUser, _ => s.Donor };

            await Assert.ThrowsAsync<InvalidOperationException>(() => admin.PromoteToAdminAsync(id, s.Admin));
            Assert.Equal(new[] { s.Admin }, await s.Db.UserRoles.Where(ur => ur.RoleId == AdminRole).Select(ur => ur.UserId).ToListAsync());
        }

        [Fact]
        public async Task Admin_Cannot_Act_On_Themselves_Or_Suspend_The_Admin()
        {
            var s = await SeedAsync();
            var admin = AdminSvc(s);
            await Assert.ThrowsAsync<InvalidOperationException>(() => admin.SuspendUserAsync(s.Admin, new SuspendUserDto { Reason = "Self" }, s.Admin));
            await Assert.ThrowsAsync<InvalidOperationException>(() => admin.ReinstateUserAsync(s.Admin, s.Admin));
            await Assert.ThrowsAsync<InvalidOperationException>(() => admin.BlockUserAsync(s.Admin, s.Admin));
            await Assert.ThrowsAsync<InvalidOperationException>(() => admin.SuspendUserAsync(s.Admin, new SuspendUserDto { Reason = "Admin" }, s.Other));
        }

        // ---------- Hospital suspension ----------

        [Fact]
        public async Task Hospital_Suspension_Notifies_Active_Requesters_And_Blocks_New_Requests_And_Transfers()
        {
            var s = await SeedAsync();
            var otherHospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Other", Email = "o@h.org", IsVerified = true };
            await s.Db.Hospitals.AddAsync(otherHospital);
            await s.Db.BloodRequests.AddRangeAsync(
                new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = s.Donor, HospitalId = s.HospitalId, BloodGroup = "A+", UnitsRequired = 1, Status = BloodRequestStatus.Verified },
                new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = s.Other, HospitalId = s.HospitalId, BloodGroup = "A+", UnitsRequired = 1, Status = BloodRequestStatus.Completed });
            await s.Db.SaveChangesAsync();

            await AdminSvc(s).SuspendHospitalAsync(s.HospitalId, new SuspendHospitalDto { Reason = "Audit" });

            var note = await s.Db.Notifications.SingleAsync(n => n.NotificationType == "RequestHospitalSuspended");
            Assert.Equal(s.Donor, note.UserId);               // only the active request's patient
            Assert.Contains("another active hospital", note.Message);

            await Assert.ThrowsAsync<InvalidOperationException>(() => new BloodRequestService(s.Db).CreateRequestAsync(s.Other,
                new CreateBloodRequestDto { HospitalId = s.HospitalId, BloodGroup = "B+", UnitsRequired = 1, Reason = "Surgery", Priority = "High" }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new TransferRequestService(s.Db).CreateTransferRequestAsync(
                new TransferRequestCreateDto { TransferType = "Request", CounterpartHospitalId = s.HospitalId, BloodGroup = "A+", UnitsRequested = 1 }, otherHospital.HospitalId));
        }

        [Fact]
        public async Task Suspended_Users_And_Hospitals_Remain_Valid_Complaint_Targets()
        {
            var s = await SeedAsync();
            await SuspendDonorAsync(s);
            await AdminSvc(s).SuspendHospitalAsync(s.HospitalId, new SuspendHospitalDto { Reason = "Audit" });
            var complaints = new ComplaintService(s.Db, s.Notify);

            Assert.NotNull(await complaints.CreateComplaintAsync(s.Other, null, new CreateComplaintDto { ComplaintType = "Other", Subject = "About a donor", Description = "Complaint description text.", TargetUserId = s.Donor }));
            Assert.NotNull(await complaints.CreateComplaintAsync(s.Other, null, new CreateComplaintDto { ComplaintType = "Hospital Service", Subject = "About a hospital", Description = "Complaint description text.", HospitalId = s.HospitalId }));
        }
    }
}
