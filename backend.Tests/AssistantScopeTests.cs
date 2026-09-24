using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Assistant;
using LifeLink.Entities;
using LifeLink.Services.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>The assistant only ever receives the signed-in user's own, role-scoped data.</summary>
    public class AssistantScopeTests
    {
        private sealed class CapturingHandler : HttpMessageHandler
        {
            public string? Body;
            public string? Key;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Key = request.Headers.TryGetValues("X-Internal-Key", out var values) ? string.Join("", values) : null;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"reply\":\"ok\",\"segments\":[{\"type\":\"account\",\"text\":\"ok\",\"sources\":[]}],\"actions\":[]}", Encoding.UTF8, "application/json")
                };
            }
        }

        private static AppDbContext NewDb()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Database.EnsureCreated();
            return db;
        }

        private static (AssistantService Service, CapturingHandler Handler) CreateService(AppDbContext db)
        {
            var handler = new CapturingHandler();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlanningAgent:BaseUrl"] = "http://supervisor.test",
                ["InternalService:ApiKey"] = "secret-key"
            }).Build();
            return (new AssistantService(db, new HttpClient(handler), config, NullLogger<AssistantService>.Instance), handler);
        }

        [Fact]
        public async Task Donor_Snapshot_Contains_Only_Their_Own_Records()
        {
            var db = NewDb();
            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "General", Email = "gh@h.org", IsVerified = true };
            var me = new User { UserId = Guid.NewGuid(), FirstName = "Me", LastName = "Donor", Email = "me@d.org", BloodGroup = "O+" };
            var other = new User { UserId = Guid.NewGuid(), FirstName = "Other", LastName = "Person", Email = "other@d.org", PhoneNumber = "0771112223" };
            var request = new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = other.UserId, HospitalId = hospital.HospitalId, BloodGroup = "O+", UnitsRequired = 2, Status = BloodRequestStatus.Approved, Reason = "Secret clinical reason", Priority = "High", ExpiryDate = DateTime.UtcNow.AddDays(3) };
            await db.Hospitals.AddAsync(hospital);
            await db.Users.AddRangeAsync(me, other);
            await db.BloodRequests.AddAsync(request);
            await db.Acceptances.AddAsync(new Acceptance { BloodRequestId = request.BloodRequestId, DonorUserId = me.UserId, Status = AcceptanceStatus.ScreeningPending });
            await db.Notifications.AddAsync(new Notification { UserId = other.UserId, Title = "Other person's alert", NotificationType = "X" });
            await db.SaveChangesAsync();

            var (service, handler) = CreateService(db);
            var reply = await service.ChatAsync(me.UserId, new[] { "User" }, me.Email, new AssistantChatRequestDto { Message = "What is my status?" });

            Assert.Equal("ok", reply.Reply);
            Assert.Equal("secret-key", handler.Key);
            Assert.Contains("\"acceptances\"", handler.Body);
            Assert.Contains("General", handler.Body);
            Assert.DoesNotContain("other@d.org", handler.Body);
            Assert.DoesNotContain("0771112223", handler.Body);
            Assert.DoesNotContain("Other person", handler.Body);
            Assert.DoesNotContain("Secret clinical reason", handler.Body);
            Assert.DoesNotContain(other.UserId.ToString(), handler.Body);
        }

        [Fact]
        public async Task Admin_Snapshot_Has_Counts_Only()
        {
            var db = NewDb();
            var admin = new User { UserId = Guid.NewGuid(), FirstName = "Admin", LastName = "One", Email = "admin@l.org" };
            await db.Users.AddAsync(admin);
            await db.Complaints.AddAsync(new Complaint { Subject = "Private complaint subject", Description = "Private details", ComplaintType = "Other" });
            await db.SaveChangesAsync();

            var (service, handler) = CreateService(db);
            await service.ChatAsync(admin.UserId, new[] { "Admin" }, admin.Email, new AssistantChatRequestDto { Message = "What needs attention?" });

            using var body = JsonDocument.Parse(handler.Body!);
            var snapshot = body.RootElement.GetProperty("snapshot");
            Assert.Equal(1, snapshot.GetProperty("openComplaints").GetInt32());
            Assert.DoesNotContain("Private complaint", handler.Body);
        }

        [Fact]
        public async Task Screening_Is_Only_For_The_Donors_Own_Active_Acceptance()
        {
            var db = NewDb();
            var owner = new User { UserId = Guid.NewGuid(), FirstName = "Owner", LastName = "D", Email = "o@d.org" };
            var intruder = new User { UserId = Guid.NewGuid(), FirstName = "Intruder", LastName = "D", Email = "i@d.org" };
            var suspended = new User { UserId = Guid.NewGuid(), FirstName = "S", LastName = "D", Email = "s@d.org", IsSuspended = true, AccountStatus = AccountStatus.Suspended };
            var acceptance = new Acceptance { BloodRequestId = Guid.NewGuid(), DonorUserId = owner.UserId, Status = AcceptanceStatus.ScreeningPending };
            var suspendedAcceptance = new Acceptance { BloodRequestId = Guid.NewGuid(), DonorUserId = suspended.UserId, Status = AcceptanceStatus.ScreeningPending };
            var closed = new Acceptance { BloodRequestId = Guid.NewGuid(), DonorUserId = owner.UserId, Status = AcceptanceStatus.Rejected };
            await db.Users.AddRangeAsync(owner, intruder, suspended);
            await db.Acceptances.AddRangeAsync(acceptance, suspendedAcceptance, closed);
            await db.SaveChangesAsync();
            var (service, handler) = CreateService(db);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ChatAsync(intruder.UserId, new[] { "User" }, intruder.Email,
                new AssistantChatRequestDto { AcceptanceId = acceptance.AcceptanceId, Message = "yes" }));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ChatAsync(owner.UserId, new[] { "Doctor" }, owner.Email,
                new AssistantChatRequestDto { AcceptanceId = acceptance.AcceptanceId, Message = "yes" }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChatAsync(suspended.UserId, new[] { "User" }, suspended.Email,
                new AssistantChatRequestDto { AcceptanceId = suspendedAcceptance.AcceptanceId, Message = "yes" }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChatAsync(owner.UserId, new[] { "User" }, owner.Email,
                new AssistantChatRequestDto { AcceptanceId = closed.AcceptanceId, Message = "yes" }));
            Assert.Null(handler.Body);

            await service.ChatAsync(owner.UserId, new[] { "User" }, owner.Email, new AssistantChatRequestDto { AcceptanceId = acceptance.AcceptanceId, Message = "yes" });
            Assert.Contains("\"mode\":\"screening\"", handler.Body);
        }
    }
}
