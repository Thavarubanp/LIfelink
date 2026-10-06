using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Controllers;
using LifeLink.Data;
using LifeLink.DTOs.Admin;
using LifeLink.DTOs.Doctors;
using LifeLink.DTOs.HospitalActivity;
using LifeLink.Entities;
using LifeLink.Services.Admin;
using LifeLink.Services.Auth;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Common;
using LifeLink.Services.Doctors;
using LifeLink.Services.HospitalActivity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Phase 2: soft-deleted doctors (uniqueness and visibility), admin-only access to deleted requests, deleted
    /// complaints, and hospital suspension only after approval.
    /// </summary>
    public class SoftDeleteAndGovernanceTests
    {
        private sealed class FakeCurrentUser : ICurrentUserService
        {
            public Guid? UserId { get; init; }
            public string? Email { get; init; }
            public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
            public bool IsAuthenticated => UserId != null;
        }

        private static AppDbContext NewContext()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated(); // seeds the roles
            return context;
        }

        private static async Task<Hospital> AddHospitalAsync(AppDbContext context, string name, ApprovalStatus status = ApprovalStatus.Approved)
        {
            var hospital = new Hospital
            {
                HospitalId = Guid.NewGuid(), Name = name, Email = $"{name.ToLower()}@test.example", IsVerified = status == ApprovalStatus.Approved, ApprovalStatus = status
            };
            context.Hospitals.Add(hospital);
            await context.SaveChangesAsync();
            return hospital;
        }

        private static CreateDoctorDto NewDoctor(Guid hospitalId, string email, string slmc) => new()
        {
            HospitalId = hospitalId, FirstName = "Test", LastName = "Doctor", Email = email, Password = "Passw0rd!", PhoneNumber = "0712345678", LicenseNumber = slmc
        };

        // ---------- Doctors ----------

        [Fact]
        public async Task Slmc_Is_Unique_Per_Hospital_And_A_Removed_Doctors_Email_And_Slmc_Can_Be_Reused()
        {
            var context = NewContext();
            var service = new DoctorService(context, new PasswordHasherService());
            var venus = await AddHospitalAsync(context, "Venus");
            var lanka = await AddHospitalAsync(context, "Lanka");

            var first = await service.CreateDoctorAsync(NewDoctor(venus.HospitalId, "doc.one@test.example", "SLMC/100"));

            // Same SLMC at the same hospital: refused (any case or spacing)
            var dup = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateDoctorAsync(NewDoctor(venus.HospitalId, "doc.two@test.example", " slmc/100 ")));
            Assert.Equal(SlmcUniquenessHelper.DuplicateMessage, dup.Message);

            // Same SLMC at another hospital, with a different email: allowed (a separate account)
            var atLanka = await service.CreateDoctorAsync(NewDoctor(lanka.HospitalId, "doc.lanka@test.example", "SLMC/100"));
            Assert.Equal("SLMC/100", atLanka.LicenseNumber);

            // The same email is never allowed twice while the doctor exists
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateDoctorAsync(NewDoctor(lanka.HospitalId, "doc.one@test.example", "SLMC/200")));

            // Once removed, the email and the SLMC number can be used again at that hospital
            await service.DeleteDoctorAsync(first.DoctorId, venus.HospitalId);
            var again = await service.CreateDoctorAsync(NewDoctor(venus.HospitalId, "doc.one@test.example", "SLMC/100"));
            Assert.NotEqual(first.DoctorId, again.DoctorId);
            Assert.Equal(2, await context.Doctors.CountAsync(d => d.HospitalId == venus.HospitalId)); // the removed row is kept
        }

        [Fact]
        public async Task A_Removed_Doctor_Is_Hidden_From_The_Hospital_But_Visible_To_The_Admin()
        {
            var context = NewContext();
            var service = new DoctorService(context, new PasswordHasherService());
            var venus = await AddHospitalAsync(context, "Venus");
            var kept = await service.CreateDoctorAsync(NewDoctor(venus.HospitalId, "kept@test.example", "SLMC/1"));
            var removed = await service.CreateDoctorAsync(NewDoctor(venus.HospitalId, "removed@test.example", "SLMC/2"));
            await service.DeleteDoctorAsync(removed.DoctorId, venus.HospitalId);

            // Hospital list (and so its pickers): only the remaining doctor
            Assert.Equal(new[] { kept.DoctorId }, (await service.GetDoctorsAsync(venus.HospitalId)).Select(d => d.DoctorId));
            // Admin list: both, the removed one flagged
            var all = await service.GetDoctorsAsync();
            Assert.Equal(2, all.Count);
            Assert.NotNull(all.Single(d => d.DoctorId == removed.DoctorId).DeletedAt);

            // A removed doctor cannot be assigned or removed again
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DoctorAssignmentRules.RequireAssignableDoctorAsync(context, removed.DoctorId, venus.HospitalId, "missing"));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteDoctorAsync(removed.DoctorId, venus.HospitalId));

            // Profile page: 404 for the hospital, visible to the Admin
            var staff = new FakeCurrentUser { UserId = Guid.NewGuid(), Email = venus.Email, Roles = new[] { "HospitalStaff" } };
            var admin = new FakeCurrentUser { UserId = Guid.NewGuid(), Roles = new[] { "Admin" } };
            Assert.IsType<NotFoundObjectResult>(await new ProfilesController(context, staff, null!).GetDoctorProfile(removed.DoctorId));
            Assert.IsType<OkObjectResult>(await new ProfilesController(context, admin, null!).GetDoctorProfile(removed.DoctorId));
        }

        // ---------- Deleted requests: only the Admin can still open them ----------

        [Fact]
        public async Task A_Deleted_Request_Is_Not_Found_For_Others_But_Visible_To_The_Admin()
        {
            var context = NewContext();
            var venus = await AddHospitalAsync(context, "Venus");
            var creator = Guid.NewGuid();
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(), PatientUserId = creator, HospitalId = venus.HospitalId, BloodGroup = "A+", UnitsRequired = 1,
                Reason = "Surgery", Priority = "Normal", Status = BloodRequestStatus.Pending, ExpiryDate = DateTime.UtcNow.AddDays(3)
            };
            context.BloodRequests.Add(request);
            await context.SaveChangesAsync();
            var service = new BloodRequestService(context);
            await service.DeleteRequestAsync(request.BloodRequestId, creator);

            BloodRequestsController ControllerFor(params string[] roles) =>
                new(service, null!, new FakeCurrentUser { UserId = Guid.NewGuid(), Roles = roles }, null!, context);

            foreach (var roles in new[] { new[] { "User" }, new[] { "HospitalStaff" }, new[] { "Doctor" } })
            {
                var notFound = Assert.IsType<NotFoundObjectResult>(await ControllerFor(roles).GetRequestById(request.BloodRequestId));
                Assert.Contains(BloodRequestService.DeletedRequestMessage, notFound.Value!.ToString());
                Assert.IsType<NotFoundObjectResult>(await ControllerFor(roles).GetRequestFulfillmentHistory(request.BloodRequestId));
            }

            var ok = Assert.IsType<OkObjectResult>(await ControllerFor("Admin").GetRequestById(request.BloodRequestId));
            Assert.Equal("Deleted", Assert.IsType<LifeLink.DTOs.BloodRequests.BloodRequestResponseDto>(ok.Value).Status);
        }

        // ---------- Complaints ----------

        [Fact]
        public async Task A_Deleted_Complaint_Accepts_No_Hospital_Activity_Report()
        {
            var context = NewContext();
            var venus = await AddHospitalAsync(context, "Venus");
            var complaint = new Complaint
            {
                ComplaintId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = venus.HospitalId, ComplaintType = "Other",
                Subject = "Test subject", Description = "Test description", DeletedAt = DateTime.UtcNow
            };
            context.Complaints.Add(complaint);
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<KeyNotFoundException>(() => new HospitalActivityService(context).SubmitActivityReportAsync(
                new SubmitActivityReportDto { HospitalId = venus.HospitalId, ComplaintId = complaint.ComplaintId, Title = "Report", Description = "Details" }, venus.HospitalId));
            Assert.Empty(context.HospitalActivityReports);
        }

        // ---------- 3.1 Hospital suspension ----------

        [Theory]
        [InlineData(ApprovalStatus.Pending)]
        [InlineData(ApprovalStatus.AwaitingAdminReview)]
        [InlineData(ApprovalStatus.Rejected)]
        public async Task A_Hospital_Waiting_For_Approval_Cannot_Be_Suspended(ApprovalStatus status)
        {
            var context = NewContext();
            var hospital = await AddHospitalAsync(context, "Waiting", status);
            var admin = new AdminService(context, new AdminNotificationService(context, new Mock<IEmailService>().Object, NullLogger<AdminNotificationService>.Instance));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => admin.SuspendHospitalAsync(hospital.HospitalId, new SuspendHospitalDto { Reason = "Audit" }));
            Assert.Contains("Only approved hospitals can be suspended", ex.Message);
            Assert.False((await context.Hospitals.FindAsync(hospital.HospitalId))!.IsSuspended);
        }

        [Fact]
        public async Task An_Approved_Hospital_Can_Be_Suspended()
        {
            var context = NewContext();
            var hospital = await AddHospitalAsync(context, "Approved");
            var admin = new AdminService(context, new AdminNotificationService(context, new Mock<IEmailService>().Object, NullLogger<AdminNotificationService>.Instance));

            await admin.SuspendHospitalAsync(hospital.HospitalId, new SuspendHospitalDto { Reason = "Audit" });

            Assert.True((await context.Hospitals.FindAsync(hospital.HospitalId))!.IsSuspended);
        }
    }
}
