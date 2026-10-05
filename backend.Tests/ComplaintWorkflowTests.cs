using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Complaints;
using LifeLink.Entities;
using LifeLink.Services.Admin;
using LifeLink.Services.Auth;
using LifeLink.Services.Complaints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using LifeLink.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace LifeLink.Tests
{
    /// <summary>
    /// Complaint workflow: role categories, alternating replies with attachments, creator-only solve/delete, notifications.
    /// </summary>
    public class ComplaintWorkflowTests
    {
        private const int UserRoleId = 1, HospitalStaffRoleId = 2, AdminRoleId = 4; // seeded by AppDbContext.HasData

        private sealed class Seed
        {
            public AppDbContext Context = null!;
            public ComplaintService Service = null!;
            public Guid Creator, Staff, Admin1; // exactly one Admin (single-admin rule)
        }

        private static async Task<Seed> SeedAsync()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();
            var s = new Seed { Context = context, Creator = Guid.NewGuid(), Staff = Guid.NewGuid(), Admin1 = Guid.NewGuid() };
            foreach (var (id, role) in new[] { (s.Creator, UserRoleId), (s.Staff, HospitalStaffRoleId), (s.Admin1, AdminRoleId) })
            {
                await context.Users.AddAsync(new User { UserId = id, FirstName = "T", LastName = "U", Email = $"{id}@t.org" });
                await context.UserRoles.AddAsync(new UserRole { UserId = id, RoleId = role });
            }
            await context.SaveChangesAsync();
            s.Service = new ComplaintService(context, new AdminNotificationService(context, new Mock<IEmailService>().Object, NullLogger<AdminNotificationService>.Instance));
            return s;
        }

        private static CreateComplaintDto Dto(string category) => new()
        {
            ComplaintType = category, Subject = "Late response", Description = "The hospital responded very late."
        };

        private static ReviewComplaintDto Reply(string text, string? file = null) => new()
        {
            Notes = text, AttachmentUrl = file == null ? null : "data:text/plain;base64,SGVsbG8=", AttachmentName = file
        };

        private static Task<int> NotificationsFor(Seed s, Guid userId) => s.Context.Notifications.CountAsync(n => n.UserId == userId);

        [Fact]
        public async Task Categories_Depend_On_Creator_Role()
        {
            var s = await SeedAsync();

            Assert.Equal("Donation Process", (await s.Service.CreateComplaintAsync(s.Creator, null, Dto("donation process"))).ComplaintType);
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.CreateComplaintAsync(s.Creator, null, Dto("Donor Misconduct")));

            Assert.Equal("Donor Misconduct", (await s.Service.CreateComplaintAsync(s.Staff, null, Dto("Donor Misconduct"))).ComplaintType);
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.CreateComplaintAsync(s.Staff, null, Dto("Donation Process")));
        }

        [Fact]
        public async Task Replies_Alternate_Admin_First_With_Attachments_And_Notifications()
        {
            var s = await SeedAsync();
            var c = await s.Service.CreateComplaintAsync(s.Creator, null, Dto("Hospital Service"));
            Assert.True(c.AwaitingAdminReply);

            // Creator cannot reply before an admin reply
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.CreatorReplyAsync(c.ComplaintId, s.Creator, Reply("Any update?")));

            // Admin replies with an attachment -> creator notified
            var afterAdmin = await s.Service.AdminReplyAsync(c.ComplaintId, s.Admin1, Reply("Please send the receipt.", "note.txt"));
            var adminMsg = afterAdmin.AuditLogs.Last();
            Assert.True(adminMsg.IsReply);
            Assert.Equal(s.Admin1, adminMsg.AdminId);
            Assert.Equal("note.txt", adminMsg.AttachmentName);
            Assert.StartsWith("data:", adminMsg.AttachmentUrl);
            Assert.True(afterAdmin.CanCreatorReply);
            Assert.Equal(1, await NotificationsFor(s, s.Creator));

            // Admin cannot reply twice in a row
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.AdminReplyAsync(c.ComplaintId, s.Admin1, Reply("Second")));

            // Only the creator may reply; creator reply notifies only the assigned admin
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Service.CreatorReplyAsync(c.ComplaintId, s.Staff, Reply("Not mine")));
            var afterCreator = await s.Service.CreatorReplyAsync(c.ComplaintId, s.Creator, Reply("Attached.", "receipt.pdf"));
            Assert.Null(afterCreator.AuditLogs.Last().AdminId);
            Assert.True(afterCreator.AwaitingAdminReply);
            Assert.Equal(1, await NotificationsFor(s, s.Admin1));
            Assert.Equal("OPEN", afterCreator.Status);
        }

        [Fact]
        public async Task Solved_Complaint_Is_Read_Only_And_Only_Creator_Can_Solve()
        {
            var s = await SeedAsync();
            var c = await s.Service.CreateComplaintAsync(s.Creator, null, Dto("Other"));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Service.SolveComplaintAsync(c.ComplaintId, s.Admin1));
            var solved = await s.Service.SolveComplaintAsync(c.ComplaintId, s.Creator);
            Assert.Equal("RESOLVED", solved.Status);
            Assert.False(solved.CanCreatorReply);
            Assert.False(solved.AwaitingAdminReply);
            // No admin assigned yet -> the Admin is notified
            Assert.Equal(1, await NotificationsFor(s, s.Admin1));

            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.AdminReplyAsync(c.ComplaintId, s.Admin1, Reply("Late reply")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.SolveComplaintAsync(c.ComplaintId, s.Creator));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Only_Creator_Deletes_In_Any_Status_And_Everything_Is_Kept_For_The_Admin(bool solveFirst)
        {
            var s = await SeedAsync();
            var c = await s.Service.CreateComplaintAsync(s.Creator, null, Dto("Other"));
            await s.Service.AdminReplyAsync(c.ComplaintId, s.Admin1, Reply("Looking into it."));
            if (solveFirst) await s.Service.SolveComplaintAsync(c.ComplaintId, s.Creator);
            var adminNotificationsBefore = await NotificationsFor(s, s.Admin1);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Service.DeleteComplaintAsync(c.ComplaintId, s.Admin1));
            await s.Service.DeleteComplaintAsync(c.ComplaintId, s.Creator);

            // Soft delete: the complaint and its thread stay; the creator no longer sees it, the Admin still does (read-only)
            Assert.NotNull((await s.Context.Complaints.SingleAsync()).DeletedAt);
            Assert.NotEqual(0, await s.Context.ComplaintAuditLogs.CountAsync());
            Assert.Empty(await s.Service.GetMyComplaintsAsync(s.Creator));
            var forAdmin = Assert.Single(await s.Service.GetComplaintsAsync());
            Assert.NotNull(forAdmin.DeletedAt);
            Assert.False(forAdmin.AwaitingAdminReply);
            Assert.False(forAdmin.CanCreatorReply);
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.AdminReplyAsync(c.ComplaintId, s.Admin1, Reply("After delete")));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => s.Service.DeleteComplaintAsync(c.ComplaintId, s.Creator));
            Assert.Equal(adminNotificationsBefore + 1, await NotificationsFor(s, s.Admin1)); // assigned admin told about the deletion
            Assert.Equal(1, await NotificationsFor(s, s.Creator)); // creator's earlier notification untouched
        }

        [Fact]
        public async Task Dismiss_Hides_Only_Own_Notification()
        {
            var s = await SeedAsync();
            var mine = new Notification { NotificationId = Guid.NewGuid(), UserId = s.Creator, Title = "a", Message = "a" };
            var other = new Notification { NotificationId = Guid.NewGuid(), UserId = s.Staff, Title = "b", Message = "b" };
            await s.Context.Notifications.AddRangeAsync(mine, other);
            await s.Context.SaveChangesAsync();
            var service = new LifeLink.Services.Notification.NotificationAgentService(s.Context, new System.Net.Http.HttpClient(),
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), NullLogger<LifeLink.Services.Notification.NotificationAgentService>.Instance);

            Assert.False(await service.DeleteNotificationAsync(other.NotificationId, s.Creator));
            Assert.True(await service.DeleteNotificationAsync(mine.NotificationId, s.Creator));
            // Soft delete: both rows stay; the dismissed one is hidden from its owner's list and unread count
            Assert.Equal(2, await s.Context.Notifications.CountAsync());
            Assert.NotNull((await s.Context.Notifications.FindAsync(mine.NotificationId))!.DismissedAt);
            Assert.Null((await s.Context.Notifications.FindAsync(other.NotificationId))!.DismissedAt);
            Assert.Empty(await service.GetNotificationsForCallerAsync(s.Creator));
            Assert.Equal(0, await service.GetUnreadCountAsync(s.Creator));
            Assert.Single(await service.GetNotificationsForCallerAsync(s.Staff));
            Assert.False(await service.DeleteNotificationAsync(mine.NotificationId, s.Creator)); // already dismissed
        }

        [Fact]
        public async Task Admin_Read_Mutations_Remain_Scoped_To_The_Admins_Own_Notifications()
        {
            var s = await SeedAsync();
            var hospital = Guid.NewGuid();
            var mine = new Notification { NotificationId = Guid.NewGuid(), UserId = s.Admin1, Title = "mine", Message = "mine" };
            var otherUser = new Notification { NotificationId = Guid.NewGuid(), UserId = s.Creator, Title = "user", Message = "user" };
            var otherHospital = new Notification { NotificationId = Guid.NewGuid(), HospitalId = hospital, Title = "hospital", Message = "hospital" };
            await s.Context.Notifications.AddRangeAsync(mine, otherUser, otherHospital);
            await s.Context.SaveChangesAsync();
            var service = new LifeLink.Services.Notification.NotificationAgentService(s.Context, new System.Net.Http.HttpClient(),
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), NullLogger<LifeLink.Services.Notification.NotificationAgentService>.Instance);

            Assert.False(await service.MarkNotificationReadAsync(otherUser.NotificationId, s.Admin1, null, true));
            Assert.False(await service.MarkNotificationReadAsync(otherHospital.NotificationId, s.Admin1, null, true));
            Assert.True(await service.MarkNotificationReadAsync(mine.NotificationId, s.Admin1, null, true));

            mine.IsRead = false;
            await s.Context.SaveChangesAsync();
            Assert.Equal(1, await service.MarkAllNotificationsReadAsync(s.Admin1, null, true));
            Assert.True(mine.IsRead);
            Assert.False(otherUser.IsRead);
            Assert.False(otherHospital.IsRead);
        }

        [Fact]
        public void Legacy_Recommendation_Write_Is_Admin_Only_Not_InternalAgent()
        {
            var method = typeof(NotificationsController).GetMethod(nameof(NotificationsController.CreateRecommendationNotification));
            var authorize = Assert.Single(method!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
            Assert.Equal("Admin", authorize.Roles);
        }
    }
}
