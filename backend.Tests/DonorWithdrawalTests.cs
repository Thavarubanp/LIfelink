using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Controllers;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using LifeLink.Services.BloodCompatibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeLink.Tests
{
    public class DonorWithdrawalTests
    {
        private sealed class World
        {
            public AppDbContext Db = null!;
            public Hospital Hospital = null!;
            public User Donor = null!;
            public User OtherDonor = null!;
            public User DoctorLogin = null!;
            public User Admin = null!;
            public Doctor Doctor = null!;
            public BloodRequest Request = null!;
            public AcceptanceService Service = null!;
        }

        private static async Task<World> SeedAsync(AcceptanceStatus status, VerificationStatus? reportStatus = null)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Database.EnsureCreated();
            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "General Hospital", Email = "general@h.org", IsVerified = true };
            var donor = new User { UserId = Guid.NewGuid(), FirstName = "Don", LastName = "Or", Email = "donor@x.org", BloodGroup = "A+" };
            var other = new User { UserId = Guid.NewGuid(), FirstName = "Other", LastName = "Donor", Email = "other@x.org", BloodGroup = "A+" };
            var doctorLogin = new User { UserId = Guid.NewGuid(), FirstName = "Act", LastName = "Ive", Email = "doctor@h.org" };
            var admin = new User { UserId = Guid.NewGuid(), FirstName = "Ad", LastName = "Min", Email = "admin@x.org" };
            var doctor = new Doctor
            {
                DoctorId = Guid.NewGuid(), UserId = doctorLogin.UserId, HospitalId = hospital.HospitalId,
                FirstName = "Act", LastName = "Ive", Email = doctorLogin.Email, IsActive = true, MustChangePassword = false
            };
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(), PatientUserId = Guid.NewGuid(), HospitalId = hospital.HospitalId,
                BloodGroup = "A+", UnitsRequired = 3, FulfilledUnits = 1,
                ReservedUnits = status == AcceptanceStatus.Verified ? 1 : 0,
                Reason = "Treatment", Priority = "High", Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(7)
            };
            var acceptance = new Acceptance
            {
                AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = donor.UserId,
                Status = status, AcceptedAt = DateTime.UtcNow.AddDays(-1)
            };
            await db.AddRangeAsync(hospital, donor, other, doctorLogin, admin, doctor, request, acceptance);
            await db.BloodRequestVerifications.AddAsync(new BloodRequestVerification
            {
                VerificationId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DoctorId = doctor.DoctorId,
                Status = VerificationStatus.Approved, UpdatedAt = DateTime.UtcNow
            });
            if (reportStatus.HasValue)
            {
                await db.DonorVerifications.AddAsync(new DonorVerification
                {
                    DonorVerificationId = Guid.NewGuid(), AcceptanceId = acceptance.AcceptanceId, DoctorId = doctor.DoctorId,
                    Status = reportStatus.Value, ReportVersion = 1, ReportJson = "{}", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();
            return new World
            {
                Db = db, Hospital = hospital, Donor = donor, OtherDonor = other, DoctorLogin = doctorLogin,
                Admin = admin, Doctor = doctor, Request = request, Service = new AcceptanceService(db, new BloodCompatibilityService())
            };
        }

        private static Acceptance Acceptance(World w) => w.Db.Acceptances.Single();

        [Theory]
        [InlineData(AcceptanceStatus.Accepted)]
        [InlineData(AcceptanceStatus.ScreeningPending)]
        [InlineData(AcceptanceStatus.ScreeningCompleted)]
        [InlineData(AcceptanceStatus.Verified)]
        public async Task Every_Active_Status_Withdraws_And_Notifies_Hospital_And_Active_Doctor(AcceptanceStatus status)
        {
            var report = status == AcceptanceStatus.ScreeningCompleted ? VerificationStatus.Pending
                : status == AcceptanceStatus.Verified ? VerificationStatus.Approved : (VerificationStatus?)null;
            var w = await SeedAsync(status, report);
            var beforeFulfilled = w.Request.FulfilledUnits;

            var result = await w.Service.CancelAcceptanceAsync(Acceptance(w).AcceptanceId, w.Donor.UserId);

            Assert.Equal("Cancelled", result.Status);
            Assert.NotNull(result.CancelledAt);
            Assert.Equal(beforeFulfilled, w.Request.FulfilledUnits);
            Assert.Equal(BloodRequestStatus.Approved, w.Request.Status);
            Assert.Equal(0, w.Request.ReservedUnits);
            var notifications = w.Db.Notifications.Where(n => n.NotificationType == "DonorWithdrew").ToList();
            Assert.Equal(2, notifications.Count);
            Assert.Single(notifications, n => n.HospitalId == w.Hospital.HospitalId && n.RecipientRole == "HospitalStaff");
            Assert.Single(notifications, n => n.UserId == w.DoctorLogin.UserId && n.RecipientRole == "Doctor");
            Assert.DoesNotContain(notifications, n => n.UserId == w.Donor.UserId || n.UserId == w.OtherDonor.UserId || n.UserId == w.Admin.UserId);
        }

        [Fact]
        public async Task ScreeningCompleted_Closes_But_Preserves_The_Pending_Report()
        {
            var w = await SeedAsync(AcceptanceStatus.ScreeningCompleted, VerificationStatus.Pending);
            var reportId = w.Db.DonorVerifications.Single().DonorVerificationId;

            await w.Service.CancelAcceptanceAsync(Acceptance(w).AcceptanceId, w.Donor.UserId);

            var report = await w.Db.DonorVerifications.FindAsync(reportId);
            Assert.NotNull(report);
            Assert.Equal(VerificationStatus.Closed, report!.Status);
            Assert.Equal("Donor withdrew.", report.Notes);
        }

        [Fact]
        public async Task Verified_Releases_Exactly_One_Reservation_And_Preserves_Approved_History()
        {
            var w = await SeedAsync(AcceptanceStatus.Verified, VerificationStatus.Approved);
            await w.Service.CancelAcceptanceAsync(Acceptance(w).AcceptanceId, w.Donor.UserId);

            Assert.Equal(0, w.Request.ReservedUnits);
            Assert.Equal(1, w.Request.FulfilledUnits);
            Assert.Equal(VerificationStatus.Approved, w.Db.DonorVerifications.Single().Status);
            Assert.True((await new LifeLink.Services.BloodRequests.BloodRequestService(w.Db).GetPublicRequestsAsync()).Single().IsAcceptingDonors);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                w.Service.CancelAcceptanceAsync(Acceptance(w).AcceptanceId, w.Donor.UserId));
            Assert.Equal(0, w.Request.ReservedUnits);
            Assert.Equal(2, w.Db.Notifications.Count(n => n.NotificationType == "DonorWithdrew"));
        }

        [Theory]
        [InlineData(AcceptanceStatus.Matched)]
        [InlineData(AcceptanceStatus.Rejected)]
        [InlineData(AcceptanceStatus.Cancelled)]
        public async Task Closed_Statuses_Cannot_Withdraw_Or_Create_Notifications(AcceptanceStatus status)
        {
            var w = await SeedAsync(status);
            var history = new RequestFulfillmentHistory
            {
                Id = Guid.NewGuid(), BloodRequestId = w.Request.BloodRequestId, AcceptanceId = Acceptance(w).AcceptanceId,
                DonorUserId = w.Donor.UserId, FulfilledAt = DateTime.UtcNow
            };
            if (status == AcceptanceStatus.Matched)
            {
                await w.Db.RequestFulfillmentHistories.AddAsync(history);
                await w.Db.SaveChangesAsync();
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                w.Service.CancelAcceptanceAsync(Acceptance(w).AcceptanceId, w.Donor.UserId));

            Assert.Equal(status, Acceptance(w).Status);
            Assert.Equal(1, w.Request.FulfilledUnits);
            Assert.Empty(w.Db.Notifications.Where(n => n.NotificationType == "DonorWithdrew"));
            if (status == AcceptanceStatus.Matched) Assert.NotNull(await w.Db.RequestFulfillmentHistories.FindAsync(history.Id));
        }

        [Fact]
        public async Task Authenticated_Identity_Ownership_Is_Enforced()
        {
            var w = await SeedAsync(AcceptanceStatus.Accepted);
            foreach (var caller in new[] { w.OtherDonor.UserId, w.DoctorLogin.UserId, w.Admin.UserId })
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    w.Service.CancelAcceptanceAsync(Acceptance(w).AcceptanceId, caller));
            }
            Assert.Equal(AcceptanceStatus.Accepted, Acceptance(w).Status);
            Assert.Empty(w.Db.Notifications);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task Inactive_Or_Deleted_Doctor_Is_Excluded_But_Hospital_Is_Still_Notified(bool deleted, bool inactive)
        {
            var w = await SeedAsync(AcceptanceStatus.Accepted);
            w.Doctor.IsActive = !inactive;
            w.Doctor.DeletedAt = deleted ? DateTime.UtcNow : null;
            await w.Db.SaveChangesAsync();

            await w.Service.CancelAcceptanceAsync(Acceptance(w).AcceptanceId, w.Donor.UserId);

            var notifications = w.Db.Notifications.Where(n => n.NotificationType == "DonorWithdrew").ToList();
            Assert.Single(notifications);
            Assert.Equal(w.Hospital.HospitalId, notifications[0].HospitalId);
        }

        [Fact]
        public async Task Suspended_Donor_Can_Withdraw_But_Cannot_Start_New_Participation()
        {
            var w = await SeedAsync(AcceptanceStatus.Accepted);
            w.Donor.IsSuspended = true;
            w.Donor.AccountStatus = AccountStatus.Suspended;
            await w.Db.SaveChangesAsync();

            Assert.Equal("Cancelled", (await w.Service.CancelAcceptanceAsync(Acceptance(w).AcceptanceId, w.Donor.UserId)).Status);

            var secondRequest = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(), PatientUserId = Guid.NewGuid(), HospitalId = w.Hospital.HospitalId,
                BloodGroup = "A+", UnitsRequired = 1, Reason = "Treatment", Priority = "High",
                Status = BloodRequestStatus.Approved, ExpiryDate = DateTime.UtcNow.AddDays(7)
            };
            await w.Db.BloodRequests.AddAsync(secondRequest);
            await w.Db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => w.Service.AcceptRequestAsync(w.Donor.UserId,
                new CreateAcceptanceDto { BloodRequestId = secondRequest.BloodRequestId, DonorBloodGroup = "A+" }));
        }

        [Fact]
        public void Suspended_Endpoints_Are_User_Only_And_Explicitly_Allowed_Through_Governance_Middleware()
        {
            foreach (var methodName in new[] { nameof(AcceptancesController.GetMyActiveWithdrawals), nameof(AcceptancesController.WithdrawWhileSuspended) })
            {
                var method = typeof(AcceptancesController).GetMethod(methodName)!;
                Assert.Equal("User", method.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
                Assert.NotNull(method.GetCustomAttribute<AllowSuspendedAccessAttribute>());
            }
            Assert.Equal("User,HospitalStaff", typeof(AcceptancesController).GetMethod(nameof(AcceptancesController.CancelAcceptance))!
                .GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        }
    }
}
