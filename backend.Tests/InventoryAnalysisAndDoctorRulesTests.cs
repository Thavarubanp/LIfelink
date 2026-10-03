using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.BloodRequests;
using LifeLink.DTOs.Inventory;
using LifeLink.DTOs.Planning;
using LifeLink.Entities;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Common;
using LifeLink.Services.Inventory;
using LifeLink.Services.Notification;
using LifeLink.Services.Planning;
using LifeLink.Services.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Phase 4: doctors pending first login (5.1), the single "below threshold" rule with exact-group help alerts and
    /// unit-count wording, the DedupeKey (Q12), and the inventory analysis lock, cooldown, run rows and status (5.5).
    /// </summary>
    public class InventoryAnalysisAndDoctorRulesTests
    {
        private static AppDbContext NewDb()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Database.EnsureCreated();
            return db;
        }

        private static Hospital AddHospital(AppDbContext db, string name)
        {
            var h = new Hospital { HospitalId = Guid.NewGuid(), Name = name, Email = $"{name.Replace(" ", "").ToLower()}@example.test", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved, ExpiryAlertDays = 5 };
            db.Hospitals.Add(h);
            return h;
        }

        private static void AddGroup(AppDbContext db, Hospital h, string group, int units, int threshold) =>
            db.BloodInventories.Add(new BloodInventory { InventoryId = Guid.NewGuid(), HospitalId = h.HospitalId, BloodGroup = group, UnitsAvailable = units, MinimumThreshold = threshold, MaximumCapacity = 100 });

        private static IConfiguration Config(bool scheduleEnabled = true) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InventoryMonitoring:Enabled"] = scheduleEnabled ? "true" : "false",
            ["InventoryMonitoring:IntervalMinutes"] = "30"
        }).Build();

        // The Supervisor is "unreachable" (null plan): the rule-based path runs, saving through the real notification service
        private static InventoryMonitor Monitor(AppDbContext db, PlanResponseDto? plan = null)
        {
            var planning = new Mock<IPlanningAgentService>();
            planning.Setup(p => p.DispatchPlanAsync(It.IsAny<PlanRequestDto>())).ReturnsAsync(plan);
            var notifications = new NotificationAgentService(db, new System.Net.Http.HttpClient(), Config(),
                NullLogger<NotificationAgentService>.Instance);
            return new InventoryMonitor(db, planning.Object, notifications, NullLogger<InventoryMonitor>.Instance);
        }

        private static InventoryAnalysisService Analysis(AppDbContext db, bool scheduleEnabled = true, PlanResponseDto? plan = null) =>
            new(db, Monitor(db, plan), Config(scheduleEnabled), NullLogger<InventoryAnalysisService>.Instance);

        // ---------- 5.1 Doctors pending first login ----------

        [Fact]
        public async Task A_Doctor_Pending_First_Login_Cannot_Be_Assigned_Or_Decide_As_Fallback()
        {
            var db = NewDb();
            var hospital = AddHospital(db, "Test Hospital A");
            var pendingLogin = new User { UserId = Guid.NewGuid(), FirstName = "New", LastName = "Doctor", Email = "new.doctor@example.test" };
            var pending = new Doctor { DoctorId = Guid.NewGuid(), HospitalId = hospital.HospitalId, UserId = pendingLogin.UserId, FirstName = "New", LastName = "Doctor", Email = "new.doctor@example.test", LicenseNumber = "SLMC/N1", IsActive = true, MustChangePassword = true };
            var patient = new User { UserId = Guid.NewGuid(), FirstName = "Test", LastName = "Patient", Email = "patient@example.test" };
            db.Users.AddRange(pendingLogin, patient);
            db.UserRoles.Add(new UserRole { UserId = pendingLogin.UserId, RoleId = 3 });
            db.Doctors.Add(pending);
            var request = new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = patient.UserId, HospitalId = hospital.HospitalId, BloodGroup = "A+", UnitsRequired = 1, Reason = "Test", Priority = "Normal", Status = BloodRequestStatus.Pending, ExpiryDate = DateTime.UtcNow.AddDays(3) };
            db.BloodRequests.Add(request);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new VerificationService(db, new Mock<INotificationAgentService>().Object).VerifyBloodRequestAsync(request.BloodRequestId, hospital.HospitalId, pending.DoctorId));
            Assert.Equal(DoctorAssignmentRules.PendingFirstLoginMessage, ex.Message);

            // Fallback lists: a removed assigned doctor's hospital donation is not shown to a doctor pending first login
            var removed = new Doctor { DoctorId = Guid.NewGuid(), HospitalId = hospital.HospitalId, FirstName = "Old", LastName = "Doctor", Email = "old@example.test", LicenseNumber = "SLMC/O1", IsActive = false, DeletedAt = DateTime.UtcNow };
            db.Doctors.Add(removed);
            request.Status = BloodRequestStatus.Approved;
            db.BloodRequestVerifications.Add(new BloodRequestVerification { BloodRequestId = request.BloodRequestId, DoctorId = removed.DoctorId });
            db.Acceptances.Add(new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = Guid.NewGuid(), DonorHospitalId = Guid.NewGuid(), Status = AcceptanceStatus.Accepted });
            await db.SaveChangesAsync();
            Assert.Empty(await new BloodRequestService(db).GetAssignedRequestsAsync(pendingLogin.UserId));

            // After the first password change the same doctor is the fallback decider
            pending.MustChangePassword = false;
            await db.SaveChangesAsync();
            Assert.Single(await new BloodRequestService(db).GetAssignedRequestsAsync(pendingLogin.UserId));
        }

        // ---------- The single "below threshold" rule ----------

        [Fact]
        public void Low_Means_Strictly_Below_The_Threshold_Everywhere()
        {
            Assert.True(InventoryRules.IsBelowThreshold(4, 5));
            Assert.False(InventoryRules.IsBelowThreshold(5, 5)); // at the threshold: not low
            Assert.False(new InventoryResponseDto { UnitsAvailable = 5, MinimumThreshold = 5 }.IsLowStock);
            Assert.True(new InventoryResponseDto { UnitsAvailable = 4, MinimumThreshold = 5 }.IsLowStock);
        }

        [Fact]
        public async Task Low_Stock_List_Uses_The_Same_Rule()
        {
            var db = NewDb();
            var h = AddHospital(db, "Test Hospital A");
            AddGroup(db, h, "A+", 4, 5);
            AddGroup(db, h, "B+", 5, 5);
            await db.SaveChangesAsync();
            var low = (await new BloodInventoryService(db).GetLowStockInventoryAsync()).ToList();
            Assert.Equal(new[] { "A+" }, low.Select(i => i.BloodGroup).ToArray());
        }

        [Fact]
        public async Task Rule_Based_Alerts_Go_To_The_Low_Hospital_And_Every_Exact_Group_Holder_Above_Its_Threshold()
        {
            var db = NewDb();
            var low = AddHospital(db, "Hospital A");
            var holder = AddHospital(db, "Hospital B");
            var atThreshold = AddHospital(db, "Hospital C");
            var otherGroup = AddHospital(db, "Hospital D");
            AddGroup(db, low, "A+", 2, 5);
            AddGroup(db, holder, "A+", 12, 5);
            AddGroup(db, atThreshold, "A+", 5, 5);
            AddGroup(db, otherGroup, "O-", 50, 5); // O- can be given to A+, but only the exact group counts
            await db.SaveChangesAsync();

            var result = await Monitor(db).RunInventoryCheckAsync();
            Assert.False(result.UsedAgents);
            Assert.Equal(2, result.LowStockAlerts);

            var toLow = await db.Notifications.SingleAsync(n => n.HospitalId == low.HospitalId);
            Assert.Equal(InventoryMonitor.ShortageType, toLow.NotificationType);
            Assert.Equal("You have only 2 units of A+ left. Hospitals holding A+: Hospital B (12 units). Consider a transfer request.", toLow.Message);
            var toHolder = await db.Notifications.SingleAsync(n => n.HospitalId == holder.HospitalId);
            Assert.Equal(InventoryMonitor.ShortageHelpType, toHolder.NotificationType);
            Assert.Equal("Hospital A has only 2 units of A+ left. You hold 12 units of A+. Consider offering a transfer.", toHolder.Message);
            Assert.Equal($"InventoryShortageHelp:A+:{low.HospitalId}", toHolder.DedupeKey);
            Assert.False(await db.Notifications.AnyAsync(n => n.HospitalId == atThreshold.HospitalId || n.HospitalId == otherGroup.HospitalId));
            Assert.DoesNotContain(await db.Notifications.Select(n => n.Message).ToListAsync(), m => m.Contains("threshold") || m.Contains("2/5"));
        }

        [Fact]
        public async Task The_DedupeKey_Stops_A_Repeat_Even_When_The_Wording_Changes()
        {
            var db = NewDb();
            var low = AddHospital(db, "Hospital A");
            AddGroup(db, low, "A+", 2, 5);
            await db.SaveChangesAsync();
            Assert.Equal(1, (await Monitor(db).RunInventoryCheckAsync()).LowStockAlerts);

            // The agent path rewrites the title (Gemini); the key is the same, so it is skipped
            var plan = new PlanResponseDto
            {
                Success = true,
                Notifications = new List<AgentNotificationDto> { new() { RecipientType = "Hospital", RecipientId = low.HospitalId.ToString(), NotificationType = InventoryMonitor.ShortageType,
                    Title = "A+ is running low", Message = "Reworded.", DedupeKey = InventoryMonitor.ShortageKey("A+") } }
            };
            var second = await Monitor(db, plan).RunInventoryCheckAsync();
            Assert.True(second.UsedAgents);
            Assert.Equal(0, second.LowStockAlerts);
            Assert.Equal(1, second.SkippedDuplicates);
            Assert.Equal(1, await db.Notifications.CountAsync());
        }

        // ---------- 5.5 Manual / scheduled analysis ----------

        [Fact]
        public async Task A_Run_Is_Recorded_Then_The_Global_Cooldown_Refuses_Another_Run()
        {
            var db = NewDb();
            var h = AddHospital(db, "Hospital A");
            AddGroup(db, h, "A+", 2, 5);
            var staff = new User { UserId = Guid.NewGuid(), FirstName = "Hospital", LastName = "Staff", Email = h.Email };
            db.Users.Add(staff);
            db.UserRoles.Add(new UserRole { UserId = staff.UserId, RoleId = 2 });
            await db.SaveChangesAsync();
            var analysis = Analysis(db);

            var run = await analysis.RunAsync(InventoryAnalysisTriggers.Manual, h.HospitalId, staff.UserId);
            Assert.Equal(InventoryAnalysisStatuses.CompletedRuleBased, run.Status);
            Assert.Equal("Hospital A", run.HospitalName);
            Assert.Equal(1, run.LowStockAlerts);
            Assert.True(await db.ActivityLogs.AnyAsync(l => l.Action == "Inventory.AnalysisRun" && l.HospitalId == h.HospitalId));

            var ex = await Assert.ThrowsAsync<ConflictException>(() => analysis.RunAsync(InventoryAnalysisTriggers.Manual, h.HospitalId, staff.UserId));
            Assert.StartsWith("Analysis ran moments ago — try again in", ex.Message);
            var status = await analysis.GetStatusAsync();
            Assert.Equal("Cooldown", status.State);
            Assert.True(status.CooldownEndsAt > status.ServerNow);
            Assert.Equal(InventoryAnalysisTriggers.Manual, status.LastRun!.Trigger);

            // The cooldown ends: the lock is free again
            var lease = await db.BackgroundJobLeases.SingleAsync(l => l.Name == InventoryAnalysisService.LeaseName);
            lease.LeasedUntil = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
            Assert.Equal("Idle", (await analysis.GetStatusAsync()).State);
        }

        [Fact]
        public async Task While_A_Run_Holds_The_Lock_Another_Is_Refused_And_A_Stale_Run_Shows_As_Interrupted()
        {
            var db = NewDb();
            Assert.True(await InventoryAnalysisService.TryTakeLockAsync(db, Guid.NewGuid()));
            var analysis = Analysis(db);
            var ex = await Assert.ThrowsAsync<ConflictException>(() => analysis.RunAsync(InventoryAnalysisTriggers.Manual));
            Assert.Equal("Analysis is already running.", ex.Message);
            Assert.Equal("Running", (await analysis.GetStatusAsync()).State);
            Assert.Null(await analysis.RunScheduledIfDueAsync()); // the scheduled run skips the busy round

            db.InventoryAnalysisRuns.Add(new InventoryAnalysisRun { StartedAt = DateTime.UtcNow.AddMinutes(-10), Trigger = InventoryAnalysisTriggers.Scheduled, Status = InventoryAnalysisStatuses.Running });
            await db.SaveChangesAsync();
            Assert.Equal("Interrupted", (await analysis.GetStatusAsync()).LastRun!.Status);
        }

        [Fact]
        public async Task The_Schedule_Reads_The_Last_Scheduled_Run_From_The_Table()
        {
            var db = NewDb();
            db.InventoryAnalysisRuns.Add(new InventoryAnalysisRun { StartedAt = DateTime.UtcNow.AddMinutes(-10), FinishedAt = DateTime.UtcNow.AddMinutes(-10), Trigger = InventoryAnalysisTriggers.Scheduled, Status = InventoryAnalysisStatuses.Completed });
            await db.SaveChangesAsync();
            var analysis = Analysis(db);

            // 10 minutes after the last scheduled run (a restart in between does not matter): not due
            Assert.Null(await analysis.RunScheduledIfDueAsync());
            var status = await analysis.GetStatusAsync();
            Assert.True(status.ScheduleEnabled);
            Assert.InRange((status.NextScheduledAt!.Value - status.ServerNow).TotalMinutes, 19, 21);

            var old = await db.InventoryAnalysisRuns.SingleAsync();
            old.StartedAt = DateTime.UtcNow.AddMinutes(-31);
            await db.SaveChangesAsync();
            var run = await analysis.RunScheduledIfDueAsync();
            Assert.NotNull(run);
            Assert.Equal(InventoryAnalysisTriggers.Scheduled, run!.Trigger);
            Assert.Equal(2, await db.InventoryAnalysisRuns.CountAsync());

            Assert.False((await Analysis(NewDb(), scheduleEnabled: false).GetStatusAsync()).ScheduleEnabled);
        }
    }
}
