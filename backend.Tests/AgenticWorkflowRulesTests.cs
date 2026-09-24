using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.DTOs.Planning;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using LifeLink.Services.BloodCompatibility;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Notification;
using LifeLink.Services.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Donor workflow rules of the agentic expansion: reserved vs fulfilled units, immutable report versions,
    /// doctor decision history, withdrawal and release, governance, emergency eligibility and AI authority limits.
    /// </summary>
    public class AgenticWorkflowRulesTests
    {
        private const string LowRiskReport = "{\"risk_level\":\"LOW\",\"recommendation\":\"Eligible\"}";

        private sealed class World
        {
            public DbContextOptions<AppDbContext> Options = null!;
            public AppDbContext Db = null!;
            public Hospital Hospital = null!;
            public Doctor AssignedDoctor = null!;
            public Doctor FallbackDoctor = null!;
            public Doctor OtherHospitalDoctor = null!;
            public User Patient = null!;
            public BloodRequest Request = null!;
            public AcceptanceService Acceptances = null!;
            public VerificationService Verification = null!;
            public NotificationAgentService Notifications = null!;
        }

        private static async Task<World> CreateWorldAsync(int unitsRequired = 5)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var db = new AppDbContext(options);
            db.Database.EnsureCreated();

            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "General Hospital", Email = "gh@h.org", IsVerified = true };
            var other = new Hospital { HospitalId = Guid.NewGuid(), Name = "Other Hospital", Email = "oh@h.org", IsVerified = true };
            Doctor NewDoctor(Hospital h, string name) => new() { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = h.HospitalId, FirstName = name, LastName = "MD", Email = $"{name}@h.org", IsActive = true };
            var assigned = NewDoctor(hospital, "Assigned");
            var fallback = NewDoctor(hospital, "Fallback");
            var outsider = NewDoctor(other, "Outsider");
            var patient = new User { UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Ient", Email = "p@x.org" };
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(), PatientUserId = patient.UserId, HospitalId = hospital.HospitalId, BloodGroup = "O+",
                UnitsRequired = unitsRequired, Reason = "Surgery", Priority = "Critical", Status = BloodRequestStatus.Approved, ExpiryDate = DateTime.UtcNow.AddDays(5)
            };

            await db.Hospitals.AddRangeAsync(hospital, other);
            await db.Doctors.AddRangeAsync(assigned, fallback, outsider);
            await db.Users.AddAsync(patient);
            await db.BloodRequests.AddAsync(request);
            await db.BloodRequestVerifications.AddAsync(new BloodRequestVerification { BloodRequestId = request.BloodRequestId, DoctorId = assigned.DoctorId, Status = VerificationStatus.Approved });
            await db.SaveChangesAsync();

            var notifications = new NotificationAgentService(db, new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<NotificationAgentService>.Instance);
            return new World
            {
                Options = options, Db = db, Hospital = hospital, AssignedDoctor = assigned, FallbackDoctor = fallback, OtherHospitalDoctor = outsider,
                Patient = patient, Request = request,
                Acceptances = new AcceptanceService(db, new BloodCompatibilityService()),
                Verification = new VerificationService(db, notifications),
                Notifications = notifications
            };
        }

        private static async Task<User> AddDonorAsync(World w, string bloodGroup = "O+", Action<User>? configure = null)
        {
            var donor = new User { UserId = Guid.NewGuid(), FirstName = "Donor", LastName = Guid.NewGuid().ToString("N")[..6], Email = $"{Guid.NewGuid():N}@d.org", BloodGroup = bloodGroup };
            configure?.Invoke(donor);
            await w.Db.Users.AddAsync(donor);
            await w.Db.SaveChangesAsync();
            return donor;
        }

        /// <summary>Accept → screening interview → report submitted (version 1) → returns the report.</summary>
        private static async Task<(Guid AcceptanceId, DonorVerification Report)> ScreenedDonorAsync(World w, User donor)
        {
            var acceptance = await w.Acceptances.AcceptRequestAsync(donor.UserId, new CreateAcceptanceDto { BloodRequestId = w.Request.BloodRequestId, DonorBloodGroup = donor.BloodGroup! });
            await w.Acceptances.UpdateScreeningStatusAsync(acceptance.AcceptanceId, AcceptanceStatus.ScreeningPending);
            var report = await w.Acceptances.SubmitScreeningReportAsync(new ScreeningReportNotificationDto
            {
                AcceptanceId = acceptance.AcceptanceId.ToString(), Summary = "No risk factors.", ReportJson = LowRiskReport
            });
            return (acceptance.AcceptanceId, report);
        }

        /// <summary>Another doctor or staff member working at the same time: their own DbContext on the same database.</summary>
        private static (AppDbContext Db, AcceptanceService Acceptances, VerificationService Verification) SecondSession(World w)
        {
            var db = new AppDbContext(w.Options);
            var notifications = new NotificationAgentService(db, new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<NotificationAgentService>.Instance);
            return (db, new AcceptanceService(db, new BloodCompatibilityService()), new VerificationService(db, notifications));
        }

        private static async Task<Guid> ApprovedDonorAsync(World w)
        {
            var (acceptanceId, report) = await ScreenedDonorAsync(w, await AddDonorAsync(w));
            await w.Verification.ApproveDonorVerificationAsync(report.DonorVerificationId, w.AssignedDoctor.UserId!.Value, "Come fasted-free, well hydrated.");
            return acceptanceId;
        }

        [Fact]
        public async Task Approval_Reserves_And_Only_Recorded_Donations_Fulfil_The_Request()
        {
            var w = await CreateWorldAsync(unitsRequired: 5);
            var approved = new List<Guid>();
            for (var i = 0; i < 5; i++) approved.Add(await ApprovedDonorAsync(w));

            await w.Acceptances.FinalizeDonorSelectionAsync(w.Request.BloodRequestId, approved.Take(2).ToList(), w.AssignedDoctor.UserId!.Value);

            // Required 5, approved 5, donated 2 -> fulfilled 2, reserved 3, remaining 3, still public
            Assert.Equal(2, w.Request.FulfilledUnits);
            Assert.Equal(3, w.Request.ReservedUnits);
            Assert.Equal(BloodRequestStatus.Approved, w.Request.Status);
            var listed = Assert.Single(await new BloodRequestService(w.Db).GetPublicRequestsAsync());
            Assert.Equal(3, listed.RemainingUnits);
            Assert.False(listed.IsAcceptingDonors); // every remaining slot is reserved: new acceptances pause

            await w.Acceptances.FinalizeDonorSelectionAsync(w.Request.BloodRequestId, approved.Skip(2).ToList(), w.AssignedDoctor.UserId!.Value);
            Assert.Equal(5, w.Request.FulfilledUnits);
            Assert.Equal(0, w.Request.ReservedUnits);
            Assert.Equal(BloodRequestStatus.Completed, w.Request.Status);
        }

        [Fact]
        public async Task No_Free_Slot_Keeps_The_Donor_On_Standby()
        {
            var w = await CreateWorldAsync(unitsRequired: 1);
            var (_, standbyReport) = await ScreenedDonorAsync(w, await AddDonorAsync(w)); // screened before the slot filled
            await ApprovedDonorAsync(w);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                w.Verification.ApproveDonorVerificationAsync(standbyReport.DonorVerificationId, w.AssignedDoctor.UserId!.Value, null));
            Assert.Contains("standby", ex.Message);
        }

        [Fact]
        public async Task Rejection_Keeps_The_Request_Public_And_The_Donor_Sees_The_Reason_And_History()
        {
            var w = await CreateWorldAsync(unitsRequired: 1);
            var donor = await AddDonorAsync(w);
            var (acceptanceId, report) = await ScreenedDonorAsync(w, donor);

            await w.Verification.RejectDonorVerificationAsync(report.DonorVerificationId, w.AssignedDoctor.UserId!.Value, "Recent tattoo (within 6 months).");

            Assert.Single(await new BloodRequestService(w.Db).GetPublicRequestsAsync());
            var mine = Assert.Single(await w.Acceptances.GetMyAcceptancesAsync(donor.UserId));
            Assert.Equal("Rejected", mine.Status);
            Assert.Equal("Recent tattoo (within 6 months).", mine.RejectionReason);
            var decision = Assert.Single(mine.ScreeningHistory);
            Assert.Equal("Rejected", decision.Status);
            Assert.Equal("Recent tattoo (within 6 months).", decision.RejectionReason);
            Assert.Equal("Dr. Assigned MD", decision.DecidedByName);
            Assert.NotNull(decision.DecidedAt);

            // Another donor can still accept
            var next = await AddDonorAsync(w);
            Assert.Equal("Accepted", (await w.Acceptances.AcceptRequestAsync(next.UserId, new CreateAcceptanceDto { BloodRequestId = w.Request.BloodRequestId, DonorBloodGroup = "O+" })).Status);
            Assert.NotEqual(acceptanceId, Guid.Empty);
        }

        [Fact]
        public async Task Withdrawal_After_Approval_Releases_The_Slot_And_Reopens_Acceptances()
        {
            var w = await CreateWorldAsync(unitsRequired: 1);
            var acceptanceId = await ApprovedDonorAsync(w);
            var donorId = (await w.Db.Acceptances.FindAsync(acceptanceId))!.DonorUserId;
            Assert.Equal(1, w.Request.ReservedUnits);

            await w.Acceptances.CancelAcceptanceAsync(acceptanceId, donorId);

            Assert.Equal(0, w.Request.ReservedUnits);
            Assert.True(Assert.Single(await new BloodRequestService(w.Db).GetPublicRequestsAsync()).IsAcceptingDonors);
            // The approval decision itself stays in history
            Assert.Equal(VerificationStatus.Approved, (await w.Db.DonorVerifications.SingleAsync(v => v.AcceptanceId == acceptanceId)).Status);
            Assert.Contains(w.Db.Notifications, n => n.NotificationType == "DonorWithdrew" && n.UserId == w.AssignedDoctor.UserId);
        }

        [Fact]
        public async Task Hospital_Staff_Or_Doctor_Can_Release_A_Reservation_With_A_Reason()
        {
            var w = await CreateWorldAsync(unitsRequired: 2);
            var acceptanceId = await ApprovedDonorAsync(w);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                w.Acceptances.ReleaseReservationAsync(acceptanceId, Guid.NewGuid(), Guid.NewGuid(), "Wrong hospital"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                w.Acceptances.ReleaseReservationAsync(acceptanceId, w.OtherHospitalDoctor.UserId!.Value, null, "Wrong hospital"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                w.Acceptances.ReleaseReservationAsync(acceptanceId, Guid.NewGuid(), w.Hospital.HospitalId, " "));

            var released = await w.Acceptances.ReleaseReservationAsync(acceptanceId, Guid.NewGuid(), w.Hospital.HospitalId, "Donor did not attend.");

            Assert.Equal("Cancelled", released.Status);
            Assert.Contains("Donor did not attend.", released.RejectionReason);
            Assert.Equal(0, w.Request.ReservedUnits);
        }

        [Fact]
        public async Task Suspended_Donor_Cannot_Be_Approved_Or_Recorded_And_Is_Released_Instead()
        {
            var w = await CreateWorldAsync(unitsRequired: 2);
            var donor = await AddDonorAsync(w);
            var (acceptanceId, report) = await ScreenedDonorAsync(w, donor);
            donor.IsSuspended = true;
            donor.AccountStatus = AccountStatus.Suspended;
            await w.Db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Verification.ApproveDonorVerificationAsync(report.DonorVerificationId, w.AssignedDoctor.UserId!.Value, null));
            await w.Acceptances.ReleaseReservationAsync(acceptanceId, w.AssignedDoctor.UserId!.Value, null, "Donor account suspended.");
            Assert.Equal(VerificationStatus.Closed, (await w.Db.DonorVerifications.FindAsync(report.DonorVerificationId))!.Status);
        }

        [Fact]
        public async Task Assigned_Doctor_Is_Primary_Same_Hospital_Doctor_Is_Fallback_Others_Are_Refused()
        {
            var w = await CreateWorldAsync();
            var (_, report) = await ScreenedDonorAsync(w, await AddDonorAsync(w));
            Assert.Equal(w.AssignedDoctor.DoctorId, report.DoctorId);
            Assert.Contains(w.Db.Notifications, n => n.NotificationType == "ScreeningReportSubmitted" && n.UserId == w.AssignedDoctor.UserId);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Verification.ApproveDonorVerificationAsync(report.DonorVerificationId, w.OtherHospitalDoctor.UserId!.Value, null));
            var approved = await w.Verification.ApproveDonorVerificationAsync(report.DonorVerificationId, w.FallbackDoctor.UserId!.Value, null);

            Assert.Equal(w.AssignedDoctor.DoctorId, approved.DoctorId);
            Assert.Equal(w.FallbackDoctor.DoctorId, approved.DecidedByDoctorId);
        }

        [Fact]
        public async Task Reports_Are_Immutable_And_Resubmission_Creates_A_New_Version()
        {
            var w = await CreateWorldAsync();
            var donor = await AddDonorAsync(w);
            var (acceptanceId, v1) = await ScreenedDonorAsync(w, donor);

            // Editing or deleting a submitted report is refused
            v1.ReportJson = "{\"risk_level\":\"HIGH\"}";
            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Db.SaveChangesAsync());
            w.Db.Entry(v1).Property(v => v.ReportJson).CurrentValue = LowRiskReport;
            w.Db.Entry(v1).Property(v => v.ReportJson).IsModified = false;
            w.Db.DonorVerifications.Remove(v1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Db.SaveChangesAsync());
            w.Db.Entry(v1).State = EntityState.Unchanged;

            // Donor updates answers while the doctor has not decided: v1 superseded (kept), v2 submitted
            await w.Acceptances.ReopenScreeningAsync(acceptanceId, donor.UserId);
            var v2 = await w.Acceptances.SubmitScreeningReportAsync(new ScreeningReportNotificationDto
            {
                AcceptanceId = acceptanceId.ToString(), Summary = "Updated.", ReportJson = LowRiskReport
            });

            Assert.Equal(2, v2.ReportVersion);
            Assert.Equal(VerificationStatus.Superseded, (await w.Db.DonorVerifications.FindAsync(v1.DonorVerificationId))!.Status);
            Assert.Equal(LowRiskReport, (await w.Db.DonorVerifications.FindAsync(v1.DonorVerificationId))!.ReportJson);
            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Verification.ApproveDonorVerificationAsync(v1.DonorVerificationId, w.AssignedDoctor.UserId!.Value, null));

            // Once decided, answers can no longer be updated and the decision cannot change
            await w.Verification.ApproveDonorVerificationAsync(v2.DonorVerificationId, w.AssignedDoctor.UserId!.Value, "OK");
            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Acceptances.ReopenScreeningAsync(acceptanceId, donor.UserId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Verification.RejectDonorVerificationAsync(v2.DonorVerificationId, w.AssignedDoctor.UserId!.Value, "Changed mind"));

            var history = Assert.Single(await w.Acceptances.GetMyAcceptancesAsync(donor.UserId)).ScreeningHistory;
            Assert.Equal(new[] { "Superseded", "Approved" }, history.Select(h => h.Status));
            Assert.Equal("OK", history[1].ApprovalNotes);
        }

        [Fact]
        public async Task Screening_Agent_Cannot_Decide_For_Doctors()
        {
            var w = await CreateWorldAsync();
            var acceptance = await w.Acceptances.AcceptRequestAsync((await AddDonorAsync(w)).UserId, new CreateAcceptanceDto { BloodRequestId = w.Request.BloodRequestId, DonorBloodGroup = "O+" });

            foreach (var status in new[] { AcceptanceStatus.ScreeningCompleted, AcceptanceStatus.Verified, AcceptanceStatus.Rejected, AcceptanceStatus.Matched })
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => w.Acceptances.UpdateScreeningStatusAsync(acceptance.AcceptanceId, status));
            }
        }

        [Fact]
        public async Task Expiry_Releases_Reservations_And_Frees_The_Donor()
        {
            var w = await CreateWorldAsync(unitsRequired: 2);
            var acceptanceId = await ApprovedDonorAsync(w);
            w.Request.ExpiryDate = DateTime.UtcNow.AddMinutes(-1);
            await w.Db.SaveChangesAsync();

            await new RequestExpiryService(w.Db, NullLogger<RequestExpiryService>.Instance).ProcessExpiredRequestsAsync();

            Assert.Equal(BloodRequestStatus.Rejected, w.Request.Status);
            Assert.Equal(0, w.Request.ReservedUnits);
            Assert.Equal(AcceptanceStatus.Cancelled, (await w.Db.Acceptances.FindAsync(acceptanceId))!.Status);
        }

        [Fact]
        public async Task Suspended_Hospital_Requests_Are_Hidden_From_Donors()
        {
            var w = await CreateWorldAsync();
            w.Hospital.IsSuspended = true;
            await w.Db.SaveChangesAsync();

            Assert.Empty(await new BloodRequestService(w.Db).GetPublicRequestsAsync());
            var donor = await AddDonorAsync(w);
            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Acceptances.AcceptRequestAsync(donor.UserId, new CreateAcceptanceDto { BloodRequestId = w.Request.BloodRequestId, DonorBloodGroup = "O+" }));
        }

        [Fact]
        public async Task Emergency_Donor_Candidates_Follow_Every_Eligibility_Rule()
        {
            var w = await CreateWorldAsync();
            var eligible = await AddDonorAsync(w, "O-");
            await AddDonorAsync(w, "A+");                                                            // incompatible with O+
            await AddDonorAsync(w, "O+", u => u.LastDonationDate = DateTime.UtcNow.AddDays(-100));    // inside 120 days
            await AddDonorAsync(w, "O+", u => { u.IsSuspended = true; u.AccountStatus = AccountStatus.Suspended; });
            await AddDonorAsync(w, "O+", u => { u.IsPermanentlyBlocked = true; u.AccountStatus = AccountStatus.Blocked; });
            await AddDonorAsync(w, "O+", u => u.AccountStatus = AccountStatus.Deleted);
            await AddDonorAsync(w, "O+", u => u.DateOfBirth = DateTime.UtcNow.AddYears(-65));        // over 60
            await AddDonorAsync(w, null!);                                                           // unknown group
            var staff = await AddDonorAsync(w, "O+");
            await w.Db.UserRoles.AddAsync(new UserRole { UserId = staff.UserId, RoleId = 2 });        // hospital staff account
            var busy = await AddDonorAsync(w, "O+");
            await w.Db.Acceptances.AddAsync(new Acceptance { BloodRequestId = Guid.NewGuid(), DonorUserId = busy.UserId, Status = AcceptanceStatus.ScreeningPending });
            var longAgo = await AddDonorAsync(w, "O+", u => u.LastDonationDate = DateTime.UtcNow.AddDays(-121));
            await w.Db.SaveChangesAsync();

            var candidates = await w.Notifications.GetEligibleDonorCandidatesAsync(w.Request.BloodRequestId, "O+", w.Patient.UserId);

            Assert.Equal(new[] { eligible.UserId, longAgo.UserId }.OrderBy(x => x), candidates.Select(c => c.UserId).OrderBy(x => x));
        }

        [Fact]
        public async Task Agent_Notifications_Are_Saved_Only_For_Recipients_The_Backend_Selected()
        {
            var w = await CreateWorldAsync();
            var allowedDonor = Guid.NewGuid();
            var saved = await w.Notifications.PersistAgentNotificationsAsync(new[]
            {
                new AgentNotificationDto { RecipientType = "Donor", RecipientId = allowedDonor.ToString(), Title = "Please donate", Message = "m" },
                new AgentNotificationDto { RecipientType = "Donor", RecipientId = Guid.NewGuid().ToString(), Title = "Injected", Message = "m" },
                new AgentNotificationDto { RecipientType = "Hospital", RecipientId = w.Hospital.HospitalId.ToString(), Title = "Not allowed", Message = "m" }
            }, new HashSet<Guid> { allowedDonor }, new HashSet<Guid>());

            Assert.Equal(1, saved);
            Assert.Equal(allowedDonor, Assert.Single(w.Db.Notifications).UserId);
        }

        [Fact]
        public async Task Two_Doctors_Approving_At_Once_Cannot_Both_Take_The_Last_Slot()
        {
            var w = await CreateWorldAsync(unitsRequired: 1);
            var (_, first) = await ScreenedDonorAsync(w, await AddDonorAsync(w));
            var (_, second) = await ScreenedDonorAsync(w, await AddDonorAsync(w));

            // The fallback doctor has the request open before the assigned doctor's approval is saved
            var fallback = SecondSession(w);
            await fallback.Db.BloodRequests.FindAsync(w.Request.BloodRequestId);

            await w.Verification.ApproveDonorVerificationAsync(first.DonorVerificationId, w.AssignedDoctor.UserId!.Value, null);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                fallback.Verification.ApproveDonorVerificationAsync(second.DonorVerificationId, w.FallbackDoctor.UserId!.Value, null));

            // Retrying on fresh data: the only slot is taken, so the second donor stays on standby
            var retry = SecondSession(w);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                retry.Verification.ApproveDonorVerificationAsync(second.DonorVerificationId, w.FallbackDoctor.UserId!.Value, null));
            Assert.Contains("standby", ex.Message);
            var request = await retry.Db.BloodRequests.AsNoTracking().SingleAsync(r => r.BloodRequestId == w.Request.BloodRequestId);
            Assert.Equal(1, request.ReservedUnits);
        }

        [Fact]
        public async Task Two_Donations_Recorded_At_Once_Are_Both_Counted()
        {
            var w = await CreateWorldAsync(unitsRequired: 2);
            var donorA = await ApprovedDonorAsync(w);
            var donorB = await ApprovedDonorAsync(w);

            var staff = SecondSession(w);
            await staff.Db.BloodRequests.FindAsync(w.Request.BloodRequestId);

            await w.Acceptances.FinalizeDonorSelectionAsync(w.Request.BloodRequestId, new List<Guid> { donorA }, w.AssignedDoctor.UserId!.Value);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                staff.Acceptances.FinalizeDonorSelectionAsync(w.Request.BloodRequestId, new List<Guid> { donorB }, w.FallbackDoctor.UserId!.Value));

            // The conflicting save changed nothing on the request; recording again on fresh data counts both donations
            var retry = SecondSession(w);
            var request = await retry.Db.BloodRequests.SingleAsync(r => r.BloodRequestId == w.Request.BloodRequestId);
            Assert.Equal(1, request.FulfilledUnits);
            Assert.Equal(1, request.ReservedUnits);

            await retry.Acceptances.FinalizeDonorSelectionAsync(w.Request.BloodRequestId, new List<Guid> { donorB }, w.FallbackDoctor.UserId!.Value);
            Assert.Equal(2, request.FulfilledUnits);
            Assert.Equal(0, request.ReservedUnits);
            Assert.Equal(BloodRequestStatus.Completed, request.Status);
        }

        [Fact]
        public async Task Approval_Saved_After_The_Request_Was_Cancelled_Is_Refused()
        {
            var w = await CreateWorldAsync(unitsRequired: 2);
            var (acceptanceId, report) = await ScreenedDonorAsync(w, await AddDonorAsync(w));

            // The doctor has the report open when the creator cancels the request
            var doctor = SecondSession(w);
            await doctor.Db.BloodRequests.FindAsync(w.Request.BloodRequestId);
            await doctor.Db.DonorVerifications.FindAsync(report.DonorVerificationId);
            await doctor.Db.Acceptances.FindAsync(acceptanceId);

            await new BloodRequestService(w.Db).CancelRequestAsync(w.Request.BloodRequestId, w.Patient.UserId);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                doctor.Verification.ApproveDonorVerificationAsync(report.DonorVerificationId, w.AssignedDoctor.UserId!.Value, null));

            var check = SecondSession(w);
            var request = await check.Db.BloodRequests.AsNoTracking().SingleAsync(r => r.BloodRequestId == w.Request.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Cancelled, request.Status);
            Assert.Equal(0, request.ReservedUnits);
            Assert.NotEqual(AcceptanceStatus.Verified, (await check.Db.Acceptances.AsNoTracking().SingleAsync(a => a.AcceptanceId == acceptanceId)).Status);
        }
    }
}
