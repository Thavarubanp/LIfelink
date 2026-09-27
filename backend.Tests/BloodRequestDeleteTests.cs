using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using LifeLink.Services.Assistant;
using LifeLink.Services.BloodCompatibility;
using LifeLink.Services.BloodRequests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Hard delete of blood requests: creator only, no active acceptance, no screened donor, no donated units; the request,
    /// its doctor assignment and inactive acceptances go in one save with notifications that do not link to it.
    /// </summary>
    public class BloodRequestDeleteTests
    {
        private const int HospitalStaffRoleId = 2;

        private sealed class Seed
        {
            public AppDbContext Context = null!;
            public Hospital Hospital = null!;
            public Hospital OtherHospital = null!;
            public User Patient = null!;
            public User Other = null!;
            public User Staff = null!;
            public Doctor Doctor = null!;
            public User DoctorLogin = null!;
            public BloodRequestService Service = null!;
        }

        private static async Task<Seed> SeedAsync()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();
            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Venus Hospital", Email = "venus@h.org", IsVerified = true };
            var other = new Hospital { HospitalId = Guid.NewGuid(), Name = "Lanka Hospital", Email = "lanka@h.org", IsVerified = true };
            var patient = new User { UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Ient", Email = "pat@h.org" };
            var otherUser = new User { UserId = Guid.NewGuid(), FirstName = "Oth", LastName = "Er", Email = "oth@h.org" };
            var staff = new User { UserId = Guid.NewGuid(), FirstName = "Venus", LastName = "Staff", Email = "venus@h.org" };
            var doctorLogin = new User { UserId = Guid.NewGuid(), FirstName = "Doc", LastName = "Tor", Email = "doc@h.org" };
            var doctor = new Doctor { DoctorId = Guid.NewGuid(), HospitalId = hospital.HospitalId, UserId = doctorLogin.UserId, FirstName = "Doc", LastName = "Tor", Email = "doc@h.org", IsActive = true };
            await context.Hospitals.AddRangeAsync(hospital, other);
            await context.Users.AddRangeAsync(patient, otherUser, staff, doctorLogin);
            await context.UserRoles.AddAsync(new UserRole { UserId = staff.UserId, RoleId = HospitalStaffRoleId });
            await context.Doctors.AddAsync(doctor);
            await context.SaveChangesAsync();
            return new Seed
            {
                Context = context, Hospital = hospital, OtherHospital = other, Patient = patient, Other = otherUser, Staff = staff,
                Doctor = doctor, DoctorLogin = doctorLogin, Service = new BloodRequestService(context)
            };
        }

        private static async Task<BloodRequest> AddRequestAsync(Seed s, BloodRequestStatus status, Guid? creator = null, string? rejectionReason = null, bool assignDoctor = true)
        {
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(), PatientUserId = creator ?? s.Patient.UserId, HospitalId = s.Hospital.HospitalId, BloodGroup = "B+",
                UnitsRequired = 2, Reason = "Surgery", Priority = "High", Status = status, RejectionReason = rejectionReason,
                CreatedAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc), ExpiryDate = DateTime.UtcNow.AddDays(3)
            };
            await s.Context.BloodRequests.AddAsync(request);
            if (assignDoctor)
            {
                await s.Context.BloodRequestVerifications.AddAsync(new BloodRequestVerification { BloodRequestId = request.BloodRequestId, DoctorId = s.Doctor.DoctorId });
            }
            await s.Context.SaveChangesAsync();
            return request;
        }

        private static async Task<Acceptance> AddAcceptanceAsync(Seed s, BloodRequest request, AcceptanceStatus status, Guid? donorHospitalId = null, bool screened = false)
        {
            var donor = new User { UserId = Guid.NewGuid(), FirstName = "Don", LastName = "Or", Email = $"{Guid.NewGuid():N}@h.org" };
            await s.Context.Users.AddAsync(donor);
            var acceptance = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = donor.UserId, DonorHospitalId = donorHospitalId, Status = status };
            await s.Context.Acceptances.AddAsync(acceptance);
            if (screened)
            {
                await s.Context.DonorVerifications.AddAsync(new DonorVerification { AcceptanceId = acceptance.AcceptanceId, ReportJson = "{}", Status = VerificationStatus.Rejected });
            }
            await s.Context.SaveChangesAsync();
            return acceptance;
        }

        private static Task<bool> ExistsAsync(Seed s, BloodRequest r) => s.Context.BloodRequests.AnyAsync(x => x.BloodRequestId == r.BloodRequestId);

        [Fact]
        public async Task Only_The_Creator_Can_Delete()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved);

            // Another user, the hospital's staff and the assigned doctor are all refused (403 in the controller)
            foreach (var caller in new[] { s.Other.UserId, s.Staff.UserId, s.DoctorLogin.UserId })
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Service.DeleteRequestAsync(request.BloodRequestId, caller));
            }
            Assert.True(await ExistsAsync(s, request));

            // A hospital-created request: the hospital is its creator
            var own = await AddRequestAsync(s, BloodRequestStatus.Verified, creator: s.Staff.UserId);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Service.DeleteRequestAsync(own.BloodRequestId, s.Patient.UserId));
            await s.Service.DeleteRequestAsync(own.BloodRequestId, s.Staff.UserId);
            Assert.False(await ExistsAsync(s, own));
        }

        public static IEnumerable<object?[]> DeletableStates() => new List<object?[]>
        {
            new object?[] { BloodRequestStatus.Pending, null, false },
            new object?[] { BloodRequestStatus.Verified, null, true },
            new object?[] { BloodRequestStatus.Rejected, "Rejected by the hospital", false },
            new object?[] { BloodRequestStatus.Approved, null, true },
            new object?[] { BloodRequestStatus.Rejected, "Rejected by the doctor", true },
            new object?[] { BloodRequestStatus.Rejected, BloodRequestService.ExpiryRejectionReason, true },
            new object?[] { BloodRequestStatus.Cancelled, null, true },
        };

        [Theory]
        [MemberData(nameof(DeletableStates))]
        public async Task The_Creator_Can_Delete_In_Every_Status_Without_Active_Acceptances(BloodRequestStatus status, string? reason, bool assigned)
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, status, rejectionReason: reason, assignDoctor: assigned);

            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            Assert.False(await ExistsAsync(s, request));
            Assert.Empty(s.Context.BloodRequestVerifications.Where(v => v.BloodRequestId == request.BloodRequestId));
        }

        [Theory]
        [InlineData(AcceptanceStatus.Accepted)]
        [InlineData(AcceptanceStatus.ScreeningPending)]
        [InlineData(AcceptanceStatus.ScreeningCompleted)]
        [InlineData(AcceptanceStatus.Verified)]
        [InlineData(AcceptanceStatus.Matched)]
        public async Task An_Active_Acceptance_Blocks_Deletion(AcceptanceStatus status)
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved);
            await AddAcceptanceAsync(s, request, status);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId));

            Assert.Equal("This request can't be deleted because a donor has accepted it.", ex.Message);
            Assert.True(await ExistsAsync(s, request));
            Assert.True((await s.Service.GetMyRequestsAsync(s.Patient.UserId)).Single().HasActiveAcceptances);
        }

        [Fact]
        public async Task An_Active_Hospital_Donation_Blocks_Deletion()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved);
            await AddAcceptanceAsync(s, request, AcceptanceStatus.Accepted, donorHospitalId: s.OtherHospital.HospitalId);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId));
            Assert.Equal(BloodRequestService.DeleteBlockedByAcceptanceMessage, ex.Message);
        }

        [Fact]
        public async Task Deletion_Is_Allowed_Again_Once_All_Donors_Withdrew_Before_Screening()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved);
            var acceptance = await AddAcceptanceAsync(s, request, AcceptanceStatus.Accepted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId));

            // The donor withdraws before any screening report
            await new AcceptanceService(s.Context, new BloodCompatibilityService()).CancelAcceptanceAsync(acceptance.AcceptanceId, acceptance.DonorUserId);
            Assert.False((await s.Service.GetMyRequestsAsync(s.Patient.UserId)).Single().HasActiveAcceptances);

            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            Assert.False(await ExistsAsync(s, request));
            Assert.Empty(s.Context.Acceptances.Where(a => a.BloodRequestId == request.BloodRequestId));
        }

        [Theory]
        [InlineData(AcceptanceStatus.Cancelled)]
        [InlineData(AcceptanceStatus.Rejected)]
        public async Task A_Screened_Donor_Blocks_Deletion_Even_After_Withdrawal_Or_Rejection(AcceptanceStatus status)
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved);
            var acceptance = await AddAcceptanceAsync(s, request, status, screened: true);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId));

            Assert.Equal("This request can't be deleted because a donor has already been screened. You can cancel it instead.", ex.Message);
            Assert.True(await ExistsAsync(s, request));
            Assert.Single(s.Context.DonorVerifications.Where(v => v.AcceptanceId == acceptance.AcceptanceId)); // reports are never deleted
            var dto = (await s.Service.GetMyRequestsAsync(s.Patient.UserId)).Single();
            Assert.True(dto.HasScreenedDonors);
            Assert.False(dto.HasActiveAcceptances);

            // The existing Cancel still works for it
            await s.Service.CancelRequestAsync(request.BloodRequestId, s.Patient.UserId);
            Assert.Equal(BloodRequestStatus.Cancelled, (await s.Context.BloodRequests.FindAsync(request.BloodRequestId))!.Status);
        }

        [Fact]
        public async Task Donated_Units_Block_Deletion()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved);
            request.FulfilledUnits = 1;
            await s.Context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId));
            Assert.Equal(BloodRequestService.DeleteBlockedByDonationMessage, ex.Message);
            Assert.True(await ExistsAsync(s, request));
        }

        [Fact]
        public async Task Dependent_Rows_Are_Removed_And_Notifications_Go_To_The_Right_People_Without_A_Link()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved);
            var withdrawn = await AddAcceptanceAsync(s, request, AcceptanceStatus.Cancelled);
            var hospitalOffer = await AddAcceptanceAsync(s, request, AcceptanceStatus.Rejected, donorHospitalId: s.OtherHospital.HospitalId);
            var unrelated = await AddRequestAsync(s, BloodRequestStatus.Approved, creator: s.Other.UserId);
            var unrelatedAcceptance = await AddAcceptanceAsync(s, unrelated, AcceptanceStatus.Accepted);

            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            Assert.False(await ExistsAsync(s, request));
            Assert.Empty(s.Context.Acceptances.Where(a => a.BloodRequestId == request.BloodRequestId));
            Assert.Empty(s.Context.BloodRequestVerifications.Where(v => v.BloodRequestId == request.BloodRequestId));
            Assert.True(await ExistsAsync(s, unrelated)); // nothing else touched
            Assert.NotNull(await s.Context.Acceptances.FindAsync(unrelatedAcceptance.AcceptanceId));

            var sent = s.Context.Notifications.Where(n => n.NotificationType == "BloodRequestDeleted").ToList();
            Assert.Equal(4, sent.Count);
            Assert.Single(sent, n => n.HospitalId == s.Hospital.HospitalId && n.UserId == null);           // hospital it was sent to
            Assert.Single(sent, n => n.UserId == s.DoctorLogin.UserId && n.RecipientRole == "Doctor");    // assigned doctor
            Assert.Single(sent, n => n.UserId == withdrawn.DonorUserId && n.RecipientRole == "Donor");     // removed donor acceptance
            Assert.Single(sent, n => n.HospitalId == s.OtherHospital.HospitalId);                         // removed hospital acceptance
            Assert.DoesNotContain(sent, n => n.UserId == s.Patient.UserId);                                // not the creator
            Assert.All(sent, n =>
            {
                Assert.Contains("Blood request for B+, 2 unit(s) at Venus Hospital (created 20 Sep 2026) was deleted by the requester.", n.Message);
                Assert.DoesNotContain(request.BloodRequestId.ToString()[..8], n.Message); // no reference to the removed request
            });
        }

        [Fact]
        public async Task A_Removed_Or_Inactive_Doctor_Is_Not_Notified()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Verified);
            s.Doctor.IsActive = false;
            await s.Context.SaveChangesAsync();

            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            Assert.DoesNotContain(s.Context.Notifications, n => n.UserId == s.DoctorLogin.UserId);
        }

        [Fact]
        public void Acceptances_Reference_Requests_With_A_Restrict_Foreign_Key()
        {
            using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var fk = context.Model.FindEntityType(typeof(Acceptance))!.GetForeignKeys()
                .Single(k => k.PrincipalEntityType.ClrType == typeof(BloodRequest));
            Assert.Equal(nameof(Acceptance.BloodRequestId), fk.Properties.Single().Name);
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }

        [Fact]
        public async Task Accepting_A_Request_That_Was_Just_Deleted_Fails_Cleanly()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved);
            var donor = new User
            {
                UserId = Guid.NewGuid(), FirstName = "Don", LastName = "Or", Email = "don@h.org", BloodGroup = "B+",
                DateOfBirth = DateTime.UtcNow.AddYears(-30), AccountStatus = AccountStatus.Active
            };
            await s.Context.Users.AddAsync(donor);
            await s.Context.SaveChangesAsync();

            // The delete wins; the later accept is refused and leaves nothing behind (in PostgreSQL the foreign key also
            // refuses an accept whose check ran before the delete committed)
            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new AcceptanceService(s.Context, new BloodCompatibilityService())
                .AcceptRequestAsync(donor.UserId, new CreateAcceptanceDto { BloodRequestId = request.BloodRequestId, DonorBloodGroup = "B+" }));
            Assert.Empty(s.Context.Acceptances.Where(a => a.BloodRequestId == request.BloodRequestId));
        }

        [Fact]
        public async Task The_Assistant_Snapshot_Never_Lists_Old_Soft_Deleted_Requests()
        {
            var s = await SeedAsync();
            await AddRequestAsync(s, BloodRequestStatus.Deleted);
            var visible = await AddRequestAsync(s, BloodRequestStatus.Pending);

            var snapshot = await new AssistantContextBuilder(s.Context).BuildAsync(s.Patient, "User", s.Patient.Email);

            var requests = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(snapshot["requests"]).ToList();
            Assert.Single(requests);
            Assert.Equal("Pending", requests[0]["status"]);
            Assert.NotEqual(BloodRequestStatus.Deleted.ToString(), requests[0]["status"]);
            Assert.True(await ExistsAsync(s, visible));
        }
    }
}
