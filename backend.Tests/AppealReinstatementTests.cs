using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Complaints;
using LifeLink.Entities;
using LifeLink.Services.Admin;
using LifeLink.Services.Appeals;
using LifeLink.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Reinstating a user (Total Donors/Patients card) or a hospital from the Admin dashboard resolves its still-open
    /// appeal exactly like an approved appeal, in the same save, with no extra notification. Emails go to a mock.
    /// </summary>
    public class AppealReinstatementTests
    {
        private sealed class World
        {
            public string DbName = null!;
            public AppDbContext Db = null!;
            public Mock<IEmailService> Email = null!;
            public AdminService Admin = null!;
            public User AdminUser = null!;
            public User Suspended = null!;
            public Hospital Hospital = null!;

            public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(DbName).Options);
        }

        private static async Task<World> SeedAsync()
        {
            var name = Guid.NewGuid().ToString();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);
            db.Database.EnsureCreated();
            var admin = new User { UserId = Guid.NewGuid(), FirstName = "Ad", LastName = "Min", Email = "admin@h.org" };
            var suspended = new User
            {
                UserId = Guid.NewGuid(), FirstName = "Sus", LastName = "Pended", Email = "sus@h.org",
                IsSuspended = true, AccountStatus = AccountStatus.Suspended, SuspensionReason = "Investigation"
            };
            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Venus Hospital", Email = "venus@h.org", IsVerified = true, IsSuspended = true, SuspensionReason = "Audit" };
            await db.Users.AddRangeAsync(admin, suspended);
            await db.UserRoles.AddAsync(new UserRole { UserId = admin.UserId, RoleId = 4 });
            await db.Hospitals.AddAsync(hospital);
            await db.SaveChangesAsync();
            var email = new Mock<IEmailService>();
            return new World
            {
                DbName = name, Db = db, Email = email, AdminUser = admin, Suspended = suspended, Hospital = hospital,
                Admin = new AdminService(db, new AdminNotificationService(db, email.Object, NullLogger<AdminNotificationService>.Instance))
            };
        }

        private static async Task<Appeal> AddAppealAsync(World w, AppealStatus status, Guid? userId, Guid? hospitalId = null, bool adminSpokeLast = false)
        {
            var appeal = new Appeal { AppealId = Guid.NewGuid(), UserId = userId, HospitalId = hospitalId, Reason = "Please review", Status = status };
            await w.Db.Appeals.AddAsync(appeal);
            await w.Db.AppealMessages.AddAsync(new AppealMessage { AppealId = appeal.AppealId, Message = "Please review", CreatedAt = DateTime.UtcNow.AddHours(-2) });
            if (adminSpokeLast)
            {
                await w.Db.AppealMessages.AddAsync(new AppealMessage { AppealId = appeal.AppealId, AdminId = w.AdminUser.UserId, Message = "[REJECTED] Not yet", CreatedAt = DateTime.UtcNow.AddHours(-1) });
            }
            await w.Db.SaveChangesAsync();
            return appeal;
        }

        private static void AssertResolved(World w, Appeal appeal, string note)
        {
            using var db = w.NewContext();
            var saved = db.Appeals.Include(a => a.Messages).Single(a => a.AppealId == appeal.AppealId);
            Assert.Equal(AppealStatus.APPROVED, saved.Status);            // the status an approved appeal gets
            Assert.Equal(w.AdminUser.UserId, saved.ReviewedByAdminId);
            Assert.NotNull(saved.ReviewedAt);
            Assert.Equal(note, saved.AdminResponse);
            var last = saved.Messages.OrderBy(m => m.CreatedAt).Last();
            Assert.Equal($"[APPROVED] {note}", last.Message);
            Assert.Equal(w.AdminUser.UserId, last.AdminId);               // an admin message: the thread is closed/read-only
        }

        [Theory]
        [InlineData(AppealStatus.PENDING)]
        [InlineData(AppealStatus.REJECTED)]
        public async Task Reinstating_A_User_Resolves_Their_Open_Appeal_Like_An_Approval(AppealStatus status)
        {
            var w = await SeedAsync();
            var appeal = await AddAppealAsync(w, status, w.Suspended.UserId, adminSpokeLast: status == AppealStatus.REJECTED);

            var result = await w.Admin.ReinstateUserAsync(w.Suspended.UserId, w.AdminUser.UserId);

            Assert.False(result.IsSuspended);
            AssertResolved(w, appeal, "Your account was reinstated by an administrator.");
        }

        [Fact]
        public async Task Reinstating_A_User_Without_An_Appeal_Works_As_Before()
        {
            var w = await SeedAsync();

            var result = await w.Admin.ReinstateUserAsync(w.Suspended.UserId, w.AdminUser.UserId);

            Assert.False(result.IsSuspended);
            Assert.Empty(w.Db.Appeals);
            Assert.Single(w.Db.Notifications.Where(n => n.UserId == w.Suspended.UserId && n.NotificationType == "UserReinstated"));
        }

        [Theory]
        [InlineData(AppealStatus.CLOSED)]
        [InlineData(AppealStatus.APPROVED)]
        public async Task Closed_Appeals_Are_Not_Changed(AppealStatus status)
        {
            var w = await SeedAsync();
            var appeal = await AddAppealAsync(w, status, w.Suspended.UserId);
            var messagesBefore = await w.Db.AppealMessages.CountAsync(m => m.AppealId == appeal.AppealId);

            await w.Admin.ReinstateUserAsync(w.Suspended.UserId, w.AdminUser.UserId);

            using var db = w.NewContext();
            var saved = db.Appeals.Single(a => a.AppealId == appeal.AppealId);
            Assert.Equal(status, saved.Status);
            Assert.Null(saved.ReviewedByAdminId);
            Assert.Equal(messagesBefore, await db.AppealMessages.CountAsync(m => m.AppealId == appeal.AppealId));
        }

        [Fact]
        public async Task Only_The_Reinstated_Users_Own_Appeal_Is_Resolved()
        {
            var w = await SeedAsync();
            var other = new User { UserId = Guid.NewGuid(), FirstName = "Oth", LastName = "Er", Email = "other@h.org", IsSuspended = true };
            await w.Db.Users.AddAsync(other);
            await w.Db.SaveChangesAsync();
            var othersAppeal = await AddAppealAsync(w, AppealStatus.PENDING, other.UserId);
            var hospitalAppeal = await AddAppealAsync(w, AppealStatus.PENDING, w.Suspended.UserId, hospitalId: w.Hospital.HospitalId);

            await w.Admin.ReinstateUserAsync(w.Suspended.UserId, w.AdminUser.UserId);

            using var db = w.NewContext();
            Assert.Equal(AppealStatus.PENDING, db.Appeals.Single(a => a.AppealId == othersAppeal.AppealId).Status);
            Assert.Equal(AppealStatus.PENDING, db.Appeals.Single(a => a.AppealId == hospitalAppeal.AppealId).Status);
        }

        [Fact]
        public async Task The_User_Gets_Only_The_Existing_Reinstatement_Notification_And_One_Email()
        {
            var w = await SeedAsync();
            await AddAppealAsync(w, AppealStatus.PENDING, w.Suspended.UserId);

            await w.Admin.ReinstateUserAsync(w.Suspended.UserId, w.AdminUser.UserId);

            var notes = w.Db.Notifications.Where(n => n.UserId == w.Suspended.UserId).ToList();
            var single = Assert.Single(notes);
            Assert.Equal("UserReinstated", single.NotificationType);
            Assert.DoesNotContain(notes, n => n.NotificationType is "AppealApproved" or "AppealRejected");
            w.Email.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
            w.Email.Verify(e => e.SendEmailAsync("sus@h.org", "Account Reinstated", It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        }

        [Theory]
        [InlineData(AppealStatus.PENDING)]
        [InlineData(AppealStatus.REJECTED)]
        public async Task Reinstating_A_Hospital_Resolves_Its_Open_Appeal_And_Sends_Only_The_Existing_Message(AppealStatus status)
        {
            var w = await SeedAsync();
            var appeal = await AddAppealAsync(w, status, null, hospitalId: w.Hospital.HospitalId);
            var closed = await AddAppealAsync(w, AppealStatus.CLOSED, null, hospitalId: w.Hospital.HospitalId);
            var usersAppeal = await AddAppealAsync(w, AppealStatus.PENDING, w.Suspended.UserId);

            var result = await w.Admin.ReinstateHospitalAsync(w.Hospital.HospitalId, w.AdminUser.UserId);

            Assert.False(result.IsSuspended);
            AssertResolved(w, appeal, "Your hospital was reinstated by an administrator.");
            using var db = w.NewContext();
            Assert.Equal(AppealStatus.CLOSED, db.Appeals.Single(a => a.AppealId == closed.AppealId).Status);
            Assert.Equal(AppealStatus.PENDING, db.Appeals.Single(a => a.AppealId == usersAppeal.AppealId).Status);
            var hospitalNotes = db.Notifications.Where(n => n.HospitalId == w.Hospital.HospitalId).ToList();
            Assert.Single(hospitalNotes);
            Assert.DoesNotContain(hospitalNotes, n => n.NotificationType is "AppealApproved" or "AppealRejected");
            w.Email.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        }

        [Fact]
        public async Task Without_An_Acting_Admin_An_Open_Appeal_Blocks_The_Reinstatement_And_Nothing_Is_Saved()
        {
            var w = await SeedAsync();
            var appeal = await AddAppealAsync(w, AppealStatus.PENDING, w.Suspended.UserId);

            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Admin.ReinstateUserAsync(w.Suspended.UserId));

            using var db = w.NewContext();
            Assert.True(db.Users.Single(u => u.UserId == w.Suspended.UserId).IsSuspended);
            Assert.Equal(AppealStatus.PENDING, db.Appeals.Single(a => a.AppealId == appeal.AppealId).Status);
            w.Email.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task A_Reply_At_The_Same_Moment_As_The_Reinstatement_Fails_Safely()
        {
            var w = await SeedAsync();
            var appeal = await AddAppealAsync(w, AppealStatus.REJECTED, w.Suspended.UserId, adminSpokeLast: true);

            // The appellant's request has the (still open) appeal loaded before the admin reinstates
            using var appellantDb = w.NewContext();
            await appellantDb.Appeals.Include(a => a.Messages).SingleAsync(a => a.AppealId == appeal.AppealId);

            await w.Admin.ReinstateUserAsync(w.Suspended.UserId, w.AdminUser.UserId);

            var appeals = new AppealService(appellantDb, new AdminNotificationService(appellantDb, w.Email.Object, NullLogger<AdminNotificationService>.Instance));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                appeals.AppellantReplyAsync(appeal.AppealId, w.Suspended.UserId, new ReviewComplaintDto { Notes = "One more detail" }));

            // The reinstatement's result stands (on PostgreSQL the failed reply is one rolled-back transaction; the
            // in-memory test database has no transactions, so only the appeal row is checked here)
            using var db = w.NewContext();
            Assert.Equal(AppealStatus.APPROVED, db.Appeals.Single(a => a.AppealId == appeal.AppealId).Status);
        }

        [Fact]
        public async Task Approving_An_Appeal_Still_Records_The_Decision_As_Before()
        {
            var w = await SeedAsync();
            var appeal = await AddAppealAsync(w, AppealStatus.PENDING, w.Suspended.UserId);
            var appeals = new AppealService(w.Db, new AdminNotificationService(w.Db, w.Email.Object, NullLogger<AdminNotificationService>.Instance));

            await appeals.ApproveAppealAsync(appeal.AppealId, w.AdminUser.UserId, new LifeLink.DTOs.Appeals.ReviewAppealDto { AdminResponse = "Evidence accepted" });

            AssertResolved(w, appeal, "Evidence accepted");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                appeals.RejectAppealAsync(appeal.AppealId, w.AdminUser.UserId, new LifeLink.DTOs.Appeals.ReviewAppealDto { AdminResponse = "x" }));
        }
    }
}
