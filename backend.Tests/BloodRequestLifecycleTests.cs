using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
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
    /// Hospital verification → assigned doctor decision → rejection/deletion → inventory on collection.
    /// </summary>
    public class BloodRequestLifecycleTests
    {
        private const int HospitalStaffRoleId = 2; // seeded by AppDbContext.HasData

        private sealed class Seed
        {
            public AppDbContext Context = null!;
            public Hospital Hospital = null!;
            public Hospital OtherHospital = null!;
            public Doctor Doctor = null!;
            public Doctor OtherDoctor = null!;
            public User Patient = null!;
            public User HospitalStaff = null!;
        }

        private static async Task<Seed> SeedAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var context = new AppDbContext(options);
            context.Database.EnsureCreated();

            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Venus Hospital", Email = "venus@lifelink.org", IsVerified = true };
            var otherHospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Other Hospital", Email = "other@lifelink.org", IsVerified = true };
            var patient = new User { UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Ient", Email = "patient@lifelink.org" };
            var staff = new User { UserId = Guid.NewGuid(), FirstName = "Venus Hospital", LastName = "Staff", Email = "venus@lifelink.org" };
            var doctor = new Doctor { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = hospital.HospitalId, FirstName = "Samara", LastName = "Silva", Email = "samara@venus.org", IsActive = true };
            var otherDoctor = new Doctor { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = otherHospital.HospitalId, FirstName = "Other", LastName = "Doc", Email = "doc@other.org", IsActive = true };

            await context.Hospitals.AddRangeAsync(hospital, otherHospital);
            await context.Users.AddRangeAsync(patient, staff);
            await context.UserRoles.AddAsync(new UserRole { UserId = staff.UserId, RoleId = HospitalStaffRoleId });
            await context.Doctors.AddRangeAsync(doctor, otherDoctor);
            await context.SaveChangesAsync();

            return new Seed { Context = context, Hospital = hospital, OtherHospital = otherHospital, Doctor = doctor, OtherDoctor = otherDoctor, Patient = patient, HospitalStaff = staff };
        }

        private static VerificationService CreateVerificationService(AppDbContext context)
        {
            var notifications = new NotificationAgentService(context, new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<NotificationAgentService>.Instance);
            return new VerificationService(context, notifications);
        }

        private static async Task<BloodRequest> AddRequestAsync(AppDbContext context, Guid creatorId, Guid hospitalId, BloodRequestStatus status = BloodRequestStatus.Pending, int units = 2)
        {
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = creatorId,
                HospitalId = hospitalId,
                BloodGroup = "A+",
                UnitsRequired = units,
                Reason = "Surgery",
                Priority = "Normal",
                Status = status,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();
            return request;
        }

        [Fact]
        public async Task Hospital_Verifies_With_Doctor_Then_Assigned_Doctor_Approves()
        {
            var s = await SeedAsync();
            var verification = CreateVerificationService(s.Context);
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId);

            await verification.VerifyBloodRequestAsync(request.BloodRequestId, s.Hospital.HospitalId, s.Doctor.DoctorId);
            Assert.Equal(BloodRequestStatus.Verified, (await s.Context.BloodRequests.FindAsync(request.BloodRequestId))!.Status);

            var assigned = await new BloodRequestService(s.Context).GetAssignedRequestsAsync(s.Doctor.UserId!.Value);
            var dto = Assert.Single(assigned);
            Assert.Equal("Dr. Samara Silva", dto.AssignedDoctorName);
            Assert.Equal("Venus Hospital", dto.HospitalName);

            var result = await verification.ApproveBloodRequestAsync(request.BloodRequestId, s.Doctor.UserId!.Value, "Cleared");
            Assert.Equal("Approved", result.Status);
            Assert.Equal(BloodRequestStatus.Approved, (await s.Context.BloodRequests.FindAsync(request.BloodRequestId))!.Status);
        }

        [Fact]
        public async Task Verify_Requires_A_Doctor_From_The_Same_Hospital()
        {
            var s = await SeedAsync();
            var verification = CreateVerificationService(s.Context);
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                verification.VerifyBloodRequestAsync(request.BloodRequestId, s.Hospital.HospitalId, Guid.Empty));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                verification.VerifyBloodRequestAsync(request.BloodRequestId, s.Hospital.HospitalId, s.OtherDoctor.DoctorId));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                verification.VerifyBloodRequestAsync(request.BloodRequestId, s.OtherHospital.HospitalId, s.OtherDoctor.DoctorId));
        }

        [Fact]
        public async Task Only_The_Assigned_Doctor_Can_Decide()
        {
            var s = await SeedAsync();
            var verification = CreateVerificationService(s.Context);
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId);
            await verification.VerifyBloodRequestAsync(request.BloodRequestId, s.Hospital.HospitalId, s.Doctor.DoctorId);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                verification.ApproveBloodRequestAsync(request.BloodRequestId, s.OtherDoctor.UserId!.Value, null));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                verification.ApproveBloodRequestAsync(request.BloodRequestId, s.Patient.UserId, null));
        }

        [Fact]
        public async Task Hospital_Rejects_Without_Doctor_But_Requires_A_Message()
        {
            var s = await SeedAsync();
            var verification = CreateVerificationService(s.Context);
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                verification.RejectBloodRequestByHospitalAsync(request.BloodRequestId, s.Hospital.HospitalId, "  "));

            await verification.RejectBloodRequestByHospitalAsync(request.BloodRequestId, s.Hospital.HospitalId, "Incomplete patient details");

            var updated = await s.Context.BloodRequests.FindAsync(request.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Rejected, updated!.Status);
            Assert.Equal("Incomplete patient details", updated.RejectionReason);
        }

        [Fact]
        public async Task Assigned_Doctor_Rejects_With_Message()
        {
            var s = await SeedAsync();
            var verification = CreateVerificationService(s.Context);
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId);
            await verification.VerifyBloodRequestAsync(request.BloodRequestId, s.Hospital.HospitalId, s.Doctor.DoctorId);

            var result = await verification.RejectBloodRequestAsync(request.BloodRequestId, s.Doctor.UserId!.Value, "Not medically required");

            Assert.Equal("Rejected", result.Status);
            var updated = await s.Context.BloodRequests.FindAsync(request.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Rejected, updated!.Status);
            Assert.Equal("Not medically required", updated.RejectionReason);
        }

        [Fact]
        public async Task Only_Creator_Can_Delete_Completed_Is_Refused_And_History_Is_Kept()
        {
            var s = await SeedAsync();
            var service = new BloodRequestService(s.Context);
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId, BloodRequestStatus.Rejected);
            var completed = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId, BloodRequestStatus.Completed);

            var acceptanceId = Guid.NewGuid();
            await s.Context.Acceptances.AddAsync(new Acceptance { AcceptanceId = acceptanceId, BloodRequestId = request.BloodRequestId, DonorUserId = s.HospitalStaff.UserId });
            await s.Context.DonorVerifications.AddAsync(new DonorVerification { AcceptanceId = acceptanceId, DoctorId = s.Doctor.DoctorId });
            await s.Context.BloodRequestVerifications.AddAsync(new BloodRequestVerification { BloodRequestId = request.BloodRequestId, DoctorId = s.Doctor.DoctorId, Status = VerificationStatus.Rejected });
            await s.Context.DonorPatientMatches.AddAsync(new DonorPatientMatch { BloodRequestId = request.BloodRequestId, DonorUserId = s.HospitalStaff.UserId, DoctorId = s.Doctor.DoctorId });
            await s.Context.RequestFulfillmentHistories.AddAsync(new RequestFulfillmentHistory { BloodRequestId = request.BloodRequestId, AcceptanceId = acceptanceId, DonorUserId = s.HospitalStaff.UserId });
            await s.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteRequestAsync(request.BloodRequestId, s.HospitalStaff.UserId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteRequestAsync(completed.BloodRequestId, s.Patient.UserId));

            await service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            // Soft delete: out of every active list, but acceptances, reports, decisions, matches and donations stay
            Assert.Equal(BloodRequestStatus.Deleted, (await s.Context.BloodRequests.FindAsync(request.BloodRequestId))!.Status);
            var acceptance = Assert.Single(s.Context.Acceptances.Where(a => a.BloodRequestId == request.BloodRequestId));
            Assert.Equal(AcceptanceStatus.Cancelled, acceptance.Status);
            Assert.Equal(VerificationStatus.Closed, Assert.Single(s.Context.DonorVerifications.Where(v => v.AcceptanceId == acceptanceId)).Status);
            Assert.Equal(VerificationStatus.Rejected, Assert.Single(s.Context.BloodRequestVerifications.Where(v => v.BloodRequestId == request.BloodRequestId)).Status);
            Assert.Single(s.Context.DonorPatientMatches.Where(m => m.BloodRequestId == request.BloodRequestId));
            Assert.Single(s.Context.RequestFulfillmentHistories.Where(h => h.BloodRequestId == request.BloodRequestId));
            Assert.Empty(await service.GetPublicRequestsAsync());
            Assert.Contains(await service.GetMyRequestsAsync(s.Patient.UserId), r => r.BloodRequestId == request.BloodRequestId && r.Status == "Deleted");
            Assert.Equal(BloodRequestStatus.Completed, (await s.Context.BloodRequests.FindAsync(completed.BloodRequestId))!.Status);
        }

        [Theory]
        [InlineData(BloodRequestStatus.Pending)]
        [InlineData(BloodRequestStatus.Verified)]
        [InlineData(BloodRequestStatus.Approved)]
        [InlineData(BloodRequestStatus.Rejected)]
        [InlineData(BloodRequestStatus.Cancelled)]
        public async Task Creator_Can_Delete_Every_Status_Except_Completed(BloodRequestStatus status)
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId, status);

            await new BloodRequestService(s.Context).DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            Assert.Equal(BloodRequestStatus.Deleted, (await s.Context.BloodRequests.FindAsync(request.BloodRequestId))!.Status);
        }

        [Fact]
        public async Task Deleting_Request_Notifies_Doctor_Hospital_And_Active_Donors_And_Keeps_Inventory()
        {
            var s = await SeedAsync();
            var doctorLogin = await AddDoctorLoginAsync(s, mustChangePassword: false);
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId, BloodRequestStatus.Approved, units: 3);
            await s.Context.BloodRequestVerifications.AddAsync(new BloodRequestVerification { BloodRequestId = request.BloodRequestId, DoctorId = s.Doctor.DoctorId, Status = VerificationStatus.Approved });

            // Donors: active, matched, cancelled, rejected; only the first two are notified
            var donors = Enumerable.Range(1, 4).Select(i => new User { UserId = Guid.NewGuid(), FirstName = $"D{i}", LastName = "X", Email = $"d{i}@lifelink.org" }).ToArray();
            await s.Context.Users.AddRangeAsync(donors);
            var statuses = new[] { AcceptanceStatus.Accepted, AcceptanceStatus.Matched, AcceptanceStatus.Cancelled, AcceptanceStatus.Rejected };
            for (var i = 0; i < 4; i++)
            {
                await s.Context.Acceptances.AddAsync(new Acceptance { BloodRequestId = request.BloodRequestId, DonorUserId = donors[i].UserId, Status = statuses[i] });
            }
            await s.Context.DonorPatientMatches.AddAsync(new DonorPatientMatch { BloodRequestId = request.BloodRequestId, DonorUserId = donors[1].UserId, DoctorId = s.Doctor.DoctorId });

            // Existing stock + transaction at the hospital must survive the delete
            var inventory = new BloodInventory { InventoryId = Guid.NewGuid(), HospitalId = s.Hospital.HospitalId, BloodGroup = "A+", UnitsAvailable = 5, MaximumCapacity = 100 };
            await s.Context.BloodInventories.AddAsync(inventory);
            await s.Context.InventoryTransactions.AddAsync(new InventoryTransaction { TransactionId = Guid.NewGuid(), InventoryId = inventory.InventoryId, TransactionType = TransactionType.StockAddition, Units = 5, Notes = $"Collected from donors for blood request {request.BloodRequestId}" });
            await s.Context.SaveChangesAsync();

            await new BloodRequestService(s.Context).DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            var sent = s.Context.Notifications.Where(n => n.NotificationType == "BloodRequestDeleted").ToList();
            Assert.Equal(4, sent.Count);
            Assert.Single(sent, n => n.UserId == doctorLogin.UserId && n.RecipientRole == "Doctor");
            Assert.Single(sent, n => n.HospitalId == s.Hospital.HospitalId && n.UserId == null && n.RecipientRole == "HospitalStaff");
            Assert.Single(sent, n => n.UserId == donors[0].UserId);
            Assert.Single(sent, n => n.UserId == donors[1].UserId); // accepted + matched -> one notification
            Assert.DoesNotContain(sent, n => n.UserId == donors[2].UserId || n.UserId == donors[3].UserId);

            Assert.Equal(BloodRequestStatus.Deleted, (await s.Context.BloodRequests.FindAsync(request.BloodRequestId))!.Status);
            var kept = s.Context.Acceptances.Where(a => a.BloodRequestId == request.BloodRequestId).ToList();
            Assert.Equal(4, kept.Count);
            Assert.Equal(AcceptanceStatus.Cancelled, kept.Single(a => a.DonorUserId == donors[0].UserId).Status);
            Assert.Equal(AcceptanceStatus.Matched, kept.Single(a => a.DonorUserId == donors[1].UserId).Status);
            Assert.Equal(5, (await s.Context.BloodInventories.FindAsync(inventory.InventoryId))!.UnitsAvailable);
            Assert.Equal(1, await s.Context.InventoryTransactions.CountAsync());
        }

        [Fact]
        public async Task Hospital_Deleting_Its_Own_Request_Does_Not_Notify_Itself()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s.Context, s.HospitalStaff.UserId, s.Hospital.HospitalId);

            await new BloodRequestService(s.Context).DeleteRequestAsync(request.BloodRequestId, s.HospitalStaff.UserId);

            Assert.Equal(BloodRequestStatus.Deleted, (await s.Context.BloodRequests.FindAsync(request.BloodRequestId))!.Status);
            Assert.Empty(s.Context.Notifications.Where(n => n.NotificationType == "BloodRequestDeleted"));
        }

        [Fact]
        public async Task Deleting_Doctor_Removes_Login_Keeps_Decided_History_And_Releases_Pending_Assignments()
        {
            var s = await SeedAsync();
            var verification = CreateVerificationService(s.Context);
            var doctorUser = new User { UserId = s.Doctor.UserId!.Value, FirstName = "Samara", LastName = "Silva", Email = "samara@venus.org" };
            await s.Context.Users.AddAsync(doctorUser);
            await s.Context.UserRoles.AddAsync(new UserRole { UserId = doctorUser.UserId, RoleId = 3 });
            await s.Context.SaveChangesAsync();

            var decided = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId);
            await verification.VerifyBloodRequestAsync(decided.BloodRequestId, s.Hospital.HospitalId, s.Doctor.DoctorId);
            await verification.ApproveBloodRequestAsync(decided.BloodRequestId, doctorUser.UserId, null);

            var awaiting = await AddRequestAsync(s.Context, s.HospitalStaff.UserId, s.Hospital.HospitalId);
            await verification.VerifyBloodRequestAsync(awaiting.BloodRequestId, s.Hospital.HospitalId, s.Doctor.DoctorId);

            var doctorService = new LifeLink.Services.Doctors.DoctorService(s.Context, new LifeLink.Services.Auth.PasswordHasherService());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => doctorService.DeleteDoctorAsync(s.Doctor.DoctorId, s.OtherHospital.HospitalId));

            await doctorService.DeleteDoctorAsync(s.Doctor.DoctorId, s.Hospital.HospitalId);

            Assert.Null(await s.Context.Doctors.FindAsync(s.Doctor.DoctorId));
            Assert.Null(await s.Context.Users.FindAsync(doctorUser.UserId));

            // Decided history survives; the undecided assignment is released back to the hospital
            var history = Assert.Single(s.Context.BloodRequestVerifications.Where(v => v.BloodRequestId == decided.BloodRequestId));
            Assert.Equal(VerificationStatus.Approved, history.Status);
            Assert.Equal(BloodRequestStatus.Approved, (await s.Context.BloodRequests.FindAsync(decided.BloodRequestId))!.Status);
            Assert.Empty(s.Context.BloodRequestVerifications.Where(v => v.BloodRequestId == awaiting.BloodRequestId));
            Assert.Equal(BloodRequestStatus.Pending, (await s.Context.BloodRequests.FindAsync(awaiting.BloodRequestId))!.Status);
        }

        // Adds the Users row + Doctor role behind the seeded doctor's login
        private static async Task<User> AddDoctorLoginAsync(Seed s, bool mustChangePassword = true)
        {
            var login = new User { UserId = s.Doctor.UserId!.Value, FirstName = "Samara", LastName = "Silva", Email = "samara@venus.org" };
            s.Doctor.MustChangePassword = mustChangePassword;
            await s.Context.Users.AddAsync(login);
            await s.Context.UserRoles.AddAsync(new UserRole { UserId = login.UserId, RoleId = 3 });
            await s.Context.SaveChangesAsync();
            return login;
        }

        private static LifeLink.Services.Doctors.DoctorService CreateDoctorService(AppDbContext context) =>
            new(context, new LifeLink.Services.Auth.PasswordHasherService());

        [Theory]
        [InlineData(true)]   // never logged in
        [InlineData(false)]  // completed first login
        public async Task Hospital_Can_Delete_Own_Doctor_Regardless_Of_Login_State(bool mustChangePassword)
        {
            var s = await SeedAsync();
            var login = await AddDoctorLoginAsync(s, mustChangePassword);

            await CreateDoctorService(s.Context).DeleteDoctorAsync(s.Doctor.DoctorId, s.Hospital.HospitalId);

            Assert.Null(await s.Context.Doctors.FindAsync(s.Doctor.DoctorId));
            Assert.Null(await s.Context.Users.FindAsync(login.UserId));
            Assert.Empty(s.Context.UserRoles.Where(ur => ur.UserId == login.UserId));
        }

        [Fact]
        public async Task Deleting_Doctor_Keeps_Rejected_Decision_And_History()
        {
            var s = await SeedAsync();
            var login = await AddDoctorLoginAsync(s);
            var verification = CreateVerificationService(s.Context);
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId);
            await verification.VerifyBloodRequestAsync(request.BloodRequestId, s.Hospital.HospitalId, s.Doctor.DoctorId);
            await verification.RejectBloodRequestAsync(request.BloodRequestId, login.UserId, "Not clinically indicated");

            await CreateDoctorService(s.Context).DeleteDoctorAsync(s.Doctor.DoctorId, s.Hospital.HospitalId);

            var updated = await s.Context.BloodRequests.FindAsync(request.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Rejected, updated!.Status);
            Assert.Equal("Not clinically indicated", updated.RejectionReason);
            var history = Assert.Single(s.Context.BloodRequestVerifications.Where(v => v.BloodRequestId == request.BloodRequestId));
            Assert.Equal(VerificationStatus.Rejected, history.Status);
            Assert.Equal("Not clinically indicated", history.Notes);
            Assert.NotNull(history.VerifiedAt);
            Assert.Null(history.DoctorId);
        }

        [Fact]
        public async Task Deleting_Doctor_Leaves_Matching_Flow_Unchanged()
        {
            var s = await SeedAsync();
            await AddDoctorLoginAsync(s);
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId, BloodRequestStatus.Approved, units: 2);
            request.FulfilledUnits = 1;
            var donor = new User { UserId = Guid.NewGuid(), FirstName = "D", LastName = "Onor", Email = "donor@lifelink.org" };
            var acceptance = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = donor.UserId, Status = AcceptanceStatus.Matched };
            await s.Context.Users.AddAsync(donor);
            await s.Context.Acceptances.AddAsync(acceptance);
            await s.Context.BloodRequestVerifications.AddAsync(new BloodRequestVerification { BloodRequestId = request.BloodRequestId, DoctorId = s.Doctor.DoctorId, Status = VerificationStatus.Approved, VerifiedAt = DateTime.UtcNow });
            await s.Context.DonorPatientMatches.AddAsync(new DonorPatientMatch { BloodRequestId = request.BloodRequestId, DonorUserId = donor.UserId, DoctorId = s.Doctor.DoctorId });
            await s.Context.DonorVerifications.AddAsync(new DonorVerification { AcceptanceId = acceptance.AcceptanceId, DoctorId = s.Doctor.DoctorId, Status = VerificationStatus.Approved });
            await s.Context.RequestFulfillmentHistories.AddAsync(new RequestFulfillmentHistory { BloodRequestId = request.BloodRequestId, AcceptanceId = acceptance.AcceptanceId, DonorUserId = donor.UserId });
            await s.Context.SaveChangesAsync();

            await CreateDoctorService(s.Context).DeleteDoctorAsync(s.Doctor.DoctorId, s.Hospital.HospitalId);

            var updated = await s.Context.BloodRequests.FindAsync(request.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Approved, updated!.Status);
            Assert.Equal(1, updated.FulfilledUnits);
            Assert.Equal(AcceptanceStatus.Matched, (await s.Context.Acceptances.FindAsync(acceptance.AcceptanceId))!.Status);
            var match = Assert.Single(s.Context.DonorPatientMatches.Where(m => m.BloodRequestId == request.BloodRequestId));
            Assert.Equal(donor.UserId, match.DonorUserId);
            Assert.Null(match.DoctorId);
            Assert.Equal(VerificationStatus.Approved, Assert.Single(s.Context.BloodRequestVerifications.Where(v => v.BloodRequestId == request.BloodRequestId)).Status);
            Assert.Equal(VerificationStatus.Approved, Assert.Single(s.Context.DonorVerifications.Where(v => v.AcceptanceId == acceptance.AcceptanceId)).Status);
            Assert.Single(s.Context.RequestFulfillmentHistories.Where(h => h.BloodRequestId == request.BloodRequestId));
        }

        [Fact]
        public async Task Doctor_Login_With_Donor_History_Is_Deactivated_Not_Deleted()
        {
            var s = await SeedAsync();
            var login = await AddDoctorLoginAsync(s, mustChangePassword: false);
            // The doctor's own login previously donated on a (different hospital's) request
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.OtherHospital.HospitalId, BloodRequestStatus.Completed, units: 1);
            var acceptance = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = login.UserId, Status = AcceptanceStatus.Matched };
            await s.Context.Acceptances.AddAsync(acceptance);
            await s.Context.DonorPatientMatches.AddAsync(new DonorPatientMatch { BloodRequestId = request.BloodRequestId, DonorUserId = login.UserId, DoctorId = s.OtherDoctor.DoctorId });
            await s.Context.SaveChangesAsync();

            await CreateDoctorService(s.Context).DeleteDoctorAsync(s.Doctor.DoctorId, s.Hospital.HospitalId);

            Assert.Null(await s.Context.Doctors.FindAsync(s.Doctor.DoctorId));
            var kept = await s.Context.Users.FindAsync(login.UserId);
            Assert.NotNull(kept);
            Assert.Equal(AccountStatus.Inactive, kept!.AccountStatus);
            Assert.Empty(s.Context.UserRoles.Where(ur => ur.UserId == login.UserId));
            Assert.Single(s.Context.DonorPatientMatches.Where(m => m.DonorUserId == login.UserId));
            Assert.Equal(AcceptanceStatus.Matched, (await s.Context.Acceptances.FindAsync(acceptance.AcceptanceId))!.Status);

            // A session still holding a token for the deactivated account is ended
            var jwt = new LifeLink.Services.Auth.JwtService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "LifeLink_Test_Signing_Key_For_Unit_Tests_Only_Must_Be_Long_Enough!",
                ["Jwt:Issuer"] = "LifeLinkAPI",
                ["Jwt:Audience"] = "LifeLinkApp"
            }).Build());
            var auth = new LifeLink.Services.Auth.AuthService(s.Context, new LifeLink.Services.Auth.PasswordHasherService(), jwt,
                new LifeLink.Services.Auth.PasswordResetService(s.Context), new Moq.Mock<LifeLink.Services.Auth.IEmailService>().Object);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.GetCurrentUserAsync(login.UserId));
        }

        [Fact]
        public async Task Complaints_Can_Target_Users_But_Not_Doctors_Or_Self()
        {
            var s = await SeedAsync();
            var doctorUser = new User { UserId = s.Doctor.UserId!.Value, FirstName = "Samara", LastName = "Silva", Email = "samara@venus.org" };
            var donor = new User { UserId = Guid.NewGuid(), FirstName = "Rude", LastName = "Donor", Email = "rude@lifelink.org" };
            await s.Context.Users.AddRangeAsync(doctorUser, donor);
            await s.Context.UserRoles.AddAsync(new UserRole { UserId = doctorUser.UserId, RoleId = 3 });
            await s.Context.SaveChangesAsync();

            var notifications = new LifeLink.Services.Admin.AdminNotificationService(
                s.Context, new Moq.Mock<LifeLink.Services.Auth.IEmailService>().Object,
                NullLogger<LifeLink.Services.Admin.AdminNotificationService>.Instance);
            var complaints = new LifeLink.Services.Complaints.ComplaintService(s.Context, notifications);
            LifeLink.DTOs.Complaints.CreateComplaintDto Dto(Guid target) => new()
            {
                ComplaintType = "Other",
                Subject = "Inappropriate behaviour",
                Description = "Detailed description of the issue.",
                TargetUserId = target
            };

            var created = await complaints.CreateComplaintAsync(s.Patient.UserId, null, Dto(donor.UserId));
            Assert.Equal(donor.UserId, created.TargetUserId);
            Assert.Equal("Rude Donor", created.TargetUserName);

            await Assert.ThrowsAsync<InvalidOperationException>(() => complaints.CreateComplaintAsync(s.Patient.UserId, null, Dto(doctorUser.UserId)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => complaints.CreateComplaintAsync(s.Patient.UserId, null, Dto(s.Patient.UserId)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => complaints.CreateComplaintAsync(s.Patient.UserId, null, Dto(s.HospitalStaff.UserId)));
        }

        [Theory]
        [InlineData(true, 2)]   // hospital-created: each recorded donation becomes a packet in the hospital's stock
        [InlineData(false, 0)]  // patient-created: inventory untouched
        public async Task Recording_Donations_Updates_Inventory_Only_For_Hospital_Created_Requests(bool hospitalCreated, int expectedUnits)
        {
            var s = await SeedAsync();
            var acceptanceService = new AcceptanceService(s.Context, new BloodCompatibilityService());
            var creatorId = hospitalCreated ? s.HospitalStaff.UserId : s.Patient.UserId;
            var request = await AddRequestAsync(s.Context, creatorId, s.Hospital.HospitalId, BloodRequestStatus.Approved, units: 2);

            // Two verified donors (existing convention: donor verification keyed by donor user id)
            var donors = new[]
            {
                new User { UserId = Guid.NewGuid(), FirstName = "D1", LastName = "X", Email = "d1@lifelink.org" },
                new User { UserId = Guid.NewGuid(), FirstName = "D2", LastName = "X", Email = "d2@lifelink.org" }
            };
            await s.Context.Users.AddRangeAsync(donors);
            foreach (var d in donors)
            {
                await s.Context.DonorVerifications.AddAsync(new DonorVerification { AcceptanceId = d.UserId, DoctorId = s.Doctor.DoctorId, Status = VerificationStatus.Approved });
            }
            await s.Context.SaveChangesAsync();

            var acceptanceIds = new List<Guid>();
            foreach (var d in donors)
            {
                var acc = await acceptanceService.AcceptRequestAsync(d.UserId, new CreateAcceptanceDto { BloodRequestId = request.BloodRequestId, DonorBloodGroup = "A+" });
                acceptanceIds.Add(acc.AcceptanceId);
            }

            await TestReservations.ReserveAsync(s.Context, acceptanceIds.ToArray());
            await acceptanceService.FinalizeDonorSelectionAsync(request.BloodRequestId, acceptanceIds, s.Doctor.DoctorId);

            var inventory = await s.Context.BloodInventories
                .FirstOrDefaultAsync(i => i.HospitalId == s.Hospital.HospitalId && i.BloodGroup == "A+");
            Assert.Equal(expectedUnits, inventory?.UnitsAvailable ?? 0);
            // One audited packet per donated unit, traceable to the acceptance it came from
            Assert.Equal(expectedUnits, await s.Context.InventoryTransactions.CountAsync(t => t.TransactionType == TransactionType.DonationCollected));
            var packets = await s.Context.BloodPackets.ToListAsync();
            Assert.Equal(expectedUnits, packets.Count);
            Assert.All(packets, p => Assert.Contains(p.SourceReferenceId!.Value, acceptanceIds));
            Assert.Equal(BloodRequestStatus.Completed, (await s.Context.BloodRequests.FindAsync(request.BloodRequestId))!.Status);
        }
    }
}
