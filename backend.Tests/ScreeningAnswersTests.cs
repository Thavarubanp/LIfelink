using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using LifeLink.Services.BloodCompatibility;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>Phase 5 (7.2, 7.3): the donor's view of their own screening answers and the edit form.</summary>
    public class ScreeningAnswersTests
    {
        private const string ReportV2 = """
            {"schema":"lifelink.screening.v2","risk_level":"HIGH","recommendation":"Requires Doctor Review","summary":"AI text","flags":[{"code":"TATTOO"}],
             "sections":[{"index":1,"title":"About you","confidential":false,"items":[{"question":"Full name","answer":"Test Donor"}],"flags":["x"]},
                         {"index":7,"title":"Final questions (confidential)","confidential":true,"items":[{"question":"Risk","answer":"No"}],"flags":[]}],
             "questionnaire":[{"question_id":"Q1","number":1,"parts":[]}],"form_answers":{"P_NAME":"Test Donor","IR_RISK":"No"}}
            """;

        private sealed class World
        {
            public AppDbContext Db = null!;
            public User Donor = null!;
            public BloodRequest Request = null!;
            public Acceptance Acceptance = null!;
            public DonorVerification Report = null!;
            public Mock<IScreeningAgentClient> Agent = new();
            public AcceptanceService Service => new(Db, new BloodCompatibilityService(), null, Agent.Object);
        }

        private static async Task<World> SeedAsync(string reportJson = ReportV2)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Database.EnsureCreated();
            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Test Hospital A", Email = "a@example.test", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved };
            var donor = new User { UserId = Guid.NewGuid(), FirstName = "Test", LastName = "Donor", Email = "donor@example.test", BloodGroup = "A+" };
            var patient = new User { UserId = Guid.NewGuid(), FirstName = "Test", LastName = "Patient", Email = "patient@example.test" };
            db.Hospitals.Add(hospital);
            db.Users.AddRange(donor, patient);
            db.UserRoles.AddRange(new UserRole { UserId = donor.UserId, RoleId = 1 }, new UserRole { UserId = patient.UserId, RoleId = 1 });
            var request = new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = patient.UserId, HospitalId = hospital.HospitalId, BloodGroup = "A+", UnitsRequired = 1, Reason = "Test", Priority = "High", Status = BloodRequestStatus.Approved, ExpiryDate = DateTime.UtcNow.AddDays(3) };
            var acceptance = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = donor.UserId, Status = AcceptanceStatus.ScreeningCompleted };
            var report = new DonorVerification { AcceptanceId = acceptance.AcceptanceId, ReportVersion = 1, ReportJson = reportJson, Status = VerificationStatus.Pending };
            db.BloodRequests.Add(request);
            db.Acceptances.Add(acceptance);
            db.DonorVerifications.Add(report);
            await db.SaveChangesAsync();
            return new World { Db = db, Donor = donor, Request = request, Acceptance = acceptance, Report = report };
        }

        private static JsonElement Answers() => JsonDocument.Parse("""{"P_NAME":"Test Donor","CONFIRM_TRUE":"Yes"}""").RootElement;

        [Fact]
        public async Task The_Donor_Sees_Their_Own_Answers_Without_AI_Fields()
        {
            var w = await SeedAsync();
            var view = await w.Service.GetScreeningAnswersAsync(w.Acceptance.AcceptanceId, w.Donor.UserId);
            Assert.True(view.CanEdit);
            Assert.Equal("Test Donor", view.Answers!.Value.GetProperty("P_NAME").GetString());
            Assert.Equal(2, view.Sections.Count);
            Assert.True(view.Sections[1].Confidential);
            var json = JsonSerializer.Serialize(view);
            foreach (var hidden in new[] { "risk_level", "AI text", "TATTOO", "Requires Doctor Review" })
            {
                Assert.DoesNotContain(hidden, json);
            }
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Service.GetScreeningAnswersAsync(w.Acceptance.AcceptanceId, w.Request.PatientUserId));
        }

        [Fact]
        public async Task Answers_Cannot_Be_Edited_After_The_Decision_While_Suspended_Or_From_The_Old_Questionnaire()
        {
            var decided = await SeedAsync();
            decided.Report.Status = VerificationStatus.Approved;
            await decided.Db.SaveChangesAsync();
            Assert.False((await decided.Service.GetScreeningAnswersAsync(decided.Acceptance.AcceptanceId, decided.Donor.UserId)).CanEdit);

            var suspended = await SeedAsync();
            suspended.Request.AdminSuspendedAt = DateTime.UtcNow;
            await suspended.Db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                suspended.Service.UpdateScreeningAnswersAsync(suspended.Acceptance.AcceptanceId, suspended.Donor.UserId, Answers()));

            var legacy = await SeedAsync("""{"schema":"lifelink.screening.v1","sections":[]}""");
            Assert.False((await legacy.Service.GetScreeningAnswersAsync(legacy.Acceptance.AcceptanceId, legacy.Donor.UserId)).CanEdit);
        }

        [Fact]
        public async Task Invalid_Answers_Or_An_Unreachable_Agent_Change_Nothing()
        {
            var w = await SeedAsync();
            w.Agent.Setup(a => a.ValidateAnswersAsync(It.IsAny<Guid>(), It.IsAny<JsonElement>())).ReturnsAsync(new List<string> { "Question 2: Weight (kg) is needed." });
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => w.Service.UpdateScreeningAnswersAsync(w.Acceptance.AcceptanceId, w.Donor.UserId, Answers()));
            Assert.Contains("Weight", ex.Message);

            w.Agent.Setup(a => a.ValidateAnswersAsync(It.IsAny<Guid>(), It.IsAny<JsonElement>())).ThrowsAsync(new ScreeningAgentUnavailableException("down"));
            await Assert.ThrowsAsync<ScreeningAgentUnavailableException>(() => w.Service.UpdateScreeningAnswersAsync(w.Acceptance.AcceptanceId, w.Donor.UserId, Answers()));

            w.Db.ChangeTracker.Clear();
            Assert.Equal(VerificationStatus.Pending, (await w.Db.DonorVerifications.SingleAsync()).Status);
            Assert.Equal(AcceptanceStatus.ScreeningCompleted, (await w.Db.Acceptances.SingleAsync()).Status);
            w.Agent.Verify(a => a.SubmitAnswersAsync(It.IsAny<Guid>(), It.IsAny<JsonElement>()), Times.Never);
        }

        [Fact]
        public async Task Saving_The_Form_Supersedes_The_Version_And_Hands_The_Answers_To_The_Agent()
        {
            var w = await SeedAsync();
            w.Agent.Setup(a => a.ValidateAnswersAsync(It.IsAny<Guid>(), It.IsAny<JsonElement>())).ReturnsAsync(new List<string>());
            w.Agent.Setup(a => a.SubmitAnswersAsync(It.IsAny<Guid>(), It.IsAny<JsonElement>())).ReturnsAsync(true);

            var result = await w.Service.UpdateScreeningAnswersAsync(w.Acceptance.AcceptanceId, w.Donor.UserId, Answers());
            Assert.Equal("ScreeningPending", result.Status);
            w.Db.ChangeTracker.Clear();
            Assert.Equal(VerificationStatus.Superseded, (await w.Db.DonorVerifications.SingleAsync()).Status);
            Assert.True(await w.Db.ActivityLogs.AnyAsync(l => l.Action == "Screening.AnswersEdited"));
            w.Agent.Verify(a => a.SubmitAnswersAsync(w.Acceptance.AcceptanceId, It.IsAny<JsonElement>()), Times.Once);
        }
    }
}
