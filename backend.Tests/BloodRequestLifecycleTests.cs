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
            var doctor = new Doctor { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = hospital.HospitalId, FirstName = "Samara", LastName = "Silva", Email = "samara@venus.org", IsActive = true, MustChangePassword = false };
            var otherDoctor = new Doctor { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = otherHospital.HospitalId, FirstName = "Other", LastName = "Doc", Email = "doc@other.org", IsActive = true, MustChangePassword = false };

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
        public async Task Only_Creator_Can_Delete_And_Screened_Or_Completed_Requests_Are_Soft_Deleted()
        {
            var s = await SeedAsync();
            var service = new BloodRequestService(s.Context);
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId, BloodRequestStatus.Rejected);
            var completed = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId, BloodRequestStatus.Completed);

            var acceptanceId = Guid.NewGuid();
            // The donor was screened and later withdrew: no active acceptance, but a screening report exists
            await s.Context.Acceptances.AddAsync(new Acceptance { AcceptanceId = acceptanceId, BloodRequestId = request.BloodRequestId, DonorUserId = s.HospitalStaff.UserId, Status = AcceptanceStatus.Cancelled });
            await s.Context.DonorVerifications.AddAsync(new DonorVerification { AcceptanceId = acceptanceId, DoctorId = s.Doctor.DoctorId });
            await s.Context.BloodRequestVerifications.AddAsync(new BloodRequestVerification { BloodRequestId = request.BloodRequestId, DoctorId = s.Doctor.DoctorId, Status = VerificationStatus.Rejected });
            await s.Context.DonorPatientMatches.AddAsync(new DonorPatientMatch { BloodRequestId = request.BloodRequestId, DonorUserId = s.HospitalStaff.UserId, DoctorId = s.Doctor.DoctorId, Status = MatchStatus.Cancelled });
            await s.Context.RequestFulfillmentHistories.AddAsync(new RequestFulfillmentHistory { BloodRequestId = request.BloodRequestId, AcceptanceId = acceptanceId, DonorUserId = s.HospitalStaff.UserId });
            await s.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteRequestAsync(request.BloodRequestId, s.HospitalStaff.UserId));
            Assert.Equal(BloodRequestStatus.Rejected, (await s.Context.BloodRequests.FindAsync(request.BloodRequestId))!.Status);

            // A completed request (units donated) and a screened request can both be deleted now (soft delete)
            await service.DeleteRequestAsync(completed.BloodRequestId, s.Patient.UserId);
            await service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            // Nothing was removed: the rows stay, only the requests are marked Deleted
            Assert.Equal(BloodRequestStatus.Deleted, (await s.Context.BloodRequests.FindAsync(completed.BloodRequestId))!.Status);
            var deleted = (await s.Context.BloodRequests.FindAsync(request.BloodRequestId))!;
            Assert.Equal(BloodRequestStatus.Deleted, deleted.Status);
            Assert.NotNull(deleted.DeletedAt);
            Assert.Single(s.Context.Acceptances.Where(a => a.BloodRequestId == request.BloodRequestId));
            Assert.Single(s.Context.DonorVerifications.Where(v => v.AcceptanceId == acceptanceId));
            Assert.Single(s.Context.BloodRequestVerifications.Where(v => v.BloodRequestId == request.BloodRequestId));
            Assert.Single(s.Context.RequestFulfillmentHistories.Where(h => h.BloodRequestId == request.BloodRequestId));
            Assert.Single(s.Context.DonorPatientMatches.Where(m => m.BloodRequestId == request.BloodRequestId));
            // The creator no longer sees them
            Assert.Empty(await service.GetMyRequestsAsync(s.Patient.UserId));
        }

        [Theory]
        [InlineData(BloodRequestStatus.Pending)]
        [InlineData(BloodRequestStatus.Verified)]
        [InlineData(BloodRequestStatus.Approved)]
        [InlineData(BloodRequestStatus.Rejected)]
        [InlineData(BloodRequestStatus.Cancelled)]
        [InlineData(BloodRequestStatus.Completed)]
        public async Task Creator_Can_Delete_Every_Status(BloodRequestStatus status)
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId, status);

            await new BloodRequestService(s.Context).DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            // Soft delete: the row stays, marked Deleted
            var row = await s.Context.BloodRequests.SingleAsync(r => r.BloodRequestId == request.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Deleted, row.Status);
            Assert.NotNull(row.DeletedAt);
        }

        [Fact]
        public async Task Deleting_Request_Notifies_Doctor_Hospital_And_Active_Donors_And_Keeps_Everything()
        {
            var s = await SeedAsync();
            var doctorLogin = await AddDoctorLoginAsync(s, mustChangePassword: false);
            var request = await AddRequestAsync(s.Context, s.Patient.UserId, s.Hospital.HospitalId, BloodRequestStatus.Approved, units: 3);
            await s.Context.BloodRequestVerifications.AddAsync(new BloodRequestVerification { BloodRequestId = request.BloodRequestId, DoctorId = s.Doctor.DoctorId, Status = VerificationStatus.Approved });

            // Two donors who already withdrew or were turned down, and one still taking part
            var donors = Enumerable.Range(1, 3).Select(i => new User { UserId = Guid.NewGuid(), FirstName = $"D{i}", LastName = "X", Email = $"d{i}@lifelink.org" }).ToArray();
            await s.Context.Users.AddRangeAsync(donors);
            await s.Context.Acceptances.AddAsync(new Acceptance { BloodRequestId = request.BloodRequestId, DonorUserId = donors[0].UserId, Status = AcceptanceStatus.Cancelled });
            await s.Context.Acceptances.AddAsync(new Acceptance { BloodRequestId = request.BloodRequestId, DonorUserId = donors[1].UserId, Status = AcceptanceStatus.Rejected });
            var active = new Acceptance { BloodRequestId = request.BloodRequestId, DonorUserId = donors[2].UserId, Status = AcceptanceStatus.ScreeningPending };
            await s.Context.Acceptances.AddAsync(active);
            await s.Context.DonorPatientMatches.AddAsync(new DonorPatientMatch { BloodRequestId = request.BloodRequestId, DonorUserId = donors[1].UserId, DoctorId = s.Doctor.DoctorId, Status = MatchStatus.Cancelled });

            // Existing stock + transaction at the hospital must survive the delete
            var inventory = new BloodInventory { InventoryId = Guid.NewGuid(), HospitalId = s.Hospital.HospitalId, BloodGroup = "A+", UnitsAvailable = 5, MaximumCapacity = 100 };
            await s.Context.BloodInventories.AddAsync(inventory);
            await s.Context.InventoryTransactions.AddAsync(new InventoryTransaction { TransactionId = Guid.NewGuid(), InventoryId = inventory.InventoryId, TransactionType = TransactionType.StockAddition, Units = 5, Notes = $"Collected from donors for blood request {request.BloodRequestId}" });
            await s.Context.SaveChangesAsync();

            await new BloodRequestService(s.Context).DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            var sent = s.Context.Notifications.Where(n => n.NotificationType == "BloodRequestDeleted").ToList();
            Assert.Equal(3, sent.Count);
            Assert.Single(sent, n => n.UserId == doctorLogin.UserId && n.RecipientRole == "Doctor");
            Assert.Single(sent, n => n.HospitalId == s.Hospital.HospitalId && n.UserId == null && n.RecipientRole == "HospitalStaff");
            Assert.Single(sent, n => n.UserId == donors[2].UserId);         // the donor still taking part
            Assert.DoesNotContain(sent, n => n.UserId == donors[0].UserId); // already withdrawn: nothing changes for them
            Assert.DoesNotContain(sent, n => n.UserId == donors[1].UserId);
            Assert.DoesNotContain(sent, n => n.UserId == s.Patient.UserId); // the creator is not notified
            Assert.All(sent, n => Assert.Contains("Blood request for A+, 3 unit(s) at Venus Hospital", n.Message));

            // Soft delete: the request, its doctor assignment, the acceptances and the match all stay; the active one is closed
            Assert.Equal(BloodRequestStatus.Deleted, (await s.Context.BloodRequests.FindAsync(request.BloodRequestId))!.Status);
            Assert.Equal(3, s.Context.Acceptances.Count(a => a.BloodRequestId == request.BloodRequestId));
            var closed = (await s.Context.Acceptances.FindAsync(active.AcceptanceId))!;
            Assert.Equal(AcceptanceStatus.Cancelled, closed.Status);
            Assert.Equal(BloodRequestService.AcceptanceClosedByDeleteReason, closed.RejectionReason);
            Assert.Single(s.Context.BloodRequestVerifications.Where(v => v.BloodRequestId == request.BloodRequestId));
            Assert.Single(s.Context.DonorPatientMatches.Where(m => m.BloodRequestId == request.BloodRequestId));
            Assert.Equal(5, (await s.Context.BloodInventories.FindAsync(inventory.InventoryId))!.UnitsAvailable);
            Assert.Equal(1, await s.Context.InventoryTransactions.CountAsync());
        }

        [Fact]
        public async Task Hospital_Deleting_Its_Own_Request_Is_Still_Notified_As_The_Affected_Hospital()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s.Context, s.HospitalStaff.UserId, s.Hospital.HospitalId);

            await new BloodRequestService(s.Context).DeleteRequestAsync(request.BloodRequestId, s.HospitalStaff.UserId);

            Assert.Equal(BloodRequestStatus.Deleted, (await s.Context.BloodRequests.FindAsync(request.BloodRequestId))!.Status);
            var notification = Assert.Single(s.Context.Notifications.Where(n => n.NotificationType == "BloodRequestDeleted"));
            Assert.Equal(s.Hospital.HospitalId, notification.HospitalId);
            Assert.Equal("HospitalStaff", notification.RecipientRole);
        }

        [Fact]
        public async Task Deleting_Doctor_Retires_Login_Keeps_Decided_History_And_Releases_Pending_Assignments()
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

            // Soft delete: the doctor row stays (removed, inactive); the login is retired and its email freed
            var removed = (await s.Context.Doctors.FindAsync(s.Doctor.DoctorId))!;
            Assert.NotNull(removed.DeletedAt);
            Assert.False(removed.IsActive);
            var retired = (await s.Context.Users.FindAsync(doctorUser.UserId))!;
            Assert.Equal(AccountStatus.Deleted, retired.AccountStatus);
            Assert.NotEqual("samara@venus.org", retired.Email);
            Assert.Empty(s.Context.UserRoles.Where(ur => ur.UserId == doctorUser.UserId));

            // Decided history survives; the undecided assignment is closed (kept) and the request goes back to the hospital
            var history = Assert.Single(s.Context.BloodRequestVerifications.Where(v => v.BloodRequestId == decided.BloodRequestId));
            Assert.Equal(VerificationStatus.Approved, history.Status);
            Assert.Equal(BloodRequestStatus.Approved, (await s.Context.BloodRequests.FindAsync(decided.BloodRequestId))!.Status);
            var closedAssignment = Assert.Single(s.Context.BloodRequestVerifications.Where(v => v.BloodRequestId == awaiting.BloodRequestId));
            Assert.Equal(VerificationStatus.Closed, closedAssignment.Status);
            Assert.Equal(BloodRequestStatus.Pending, (await s.Context.BloodRequests.FindAsync(awaiting.BloodRequestId))!.Status);

            // The hospital no longer sees the doctor, and the request no longer shows an assigned doctor
            Assert.Empty(await doctorService.GetDoctorsAsync(s.Hospital.HospitalId));
            Assert.Null((await new BloodRequestService(s.Context).GetRequestByIdAsync(awaiting.BloodRequestId))!.AssignedDoctorId);
            // The decided request shows "Removed doctor"
            Assert.Equal("Removed doctor", (await new BloodRequestService(s.Context).GetRequestByIdAsync(decided.BloodRequestId))!.AssignedDoctorName);
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

            Assert.NotNull((await s.Context.Doctors.FindAsync(s.Doctor.DoctorId))!.DeletedAt);
            Assert.Equal(AccountStatus.Deleted, (await s.Context.Users.FindAsync(login.UserId))!.AccountStatus);
            Assert.Empty(s.Context.UserRoles.Where(ur => ur.UserId == login.UserId));
        }

        [Fact]
        public async Task Deleting_Doctor_Keeps_Rejected_Decision_And_History()
        {
            var s = await SeedAsync();
            var login = await AddDoctorLoginAsync(s, mustChangePassword: false); // only doctors past first login can be assigned (5.1)
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
            Assert.Equal(s.Doctor.DoctorId, history.DoctorId); // the doctor row is kept, so the link stays
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
            Assert.Equal(s.Doctor.DoctorId, match.DoctorId); // the doctor row is kept, so the link stays
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

            Assert.NotNull((await s.Context.Doctors.FindAsync(s.Doctor.DoctorId))!.DeletedAt);
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
