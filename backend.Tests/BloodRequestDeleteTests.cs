using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.DTOs.BloodRequests;
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
    /// Soft delete of blood requests: only the creator, in any status, even with active donors. Nothing is removed:
    /// the request becomes Deleted (hidden from every list except the Admin's), active acceptances are closed, screening
    /// reports are kept, and the hospital, the assigned doctor and the donors/hospitals whose acceptances were closed are
    /// notified in the same save.
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
            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Venus Hospital", Email = "venus@h.org", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved };
            var other = new Hospital { HospitalId = Guid.NewGuid(), Name = "Lanka Hospital", Email = "lanka@h.org", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved };
            var patient = new User { UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Ient", Email = "pat@h.org" };
            var otherUser = new User { UserId = Guid.NewGuid(), FirstName = "Oth", LastName = "Er", Email = "oth@h.org" };
            var staff = new User { UserId = Guid.NewGuid(), FirstName = "Venus", LastName = "Staff", Email = "venus@h.org" };
            var doctorLogin = new User { UserId = Guid.NewGuid(), FirstName = "Doc", LastName = "Tor", Email = "doc@h.org" };
            var doctor = new Doctor { DoctorId = Guid.NewGuid(), HospitalId = hospital.HospitalId, UserId = doctorLogin.UserId, FirstName = "Doc", LastName = "Tor", Email = "doc@h.org", IsActive = true, MustChangePassword = false };
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

        private static async Task<BloodRequest> AddRequestAsync(Seed s, BloodRequestStatus status, Guid? creator = null, string? rejectionReason = null,
            bool assignDoctor = true, int reserved = 0, int fulfilled = 0)
        {
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(), PatientUserId = creator ?? s.Patient.UserId, HospitalId = s.Hospital.HospitalId, BloodGroup = "B+",
                UnitsRequired = 2, ReservedUnits = reserved, FulfilledUnits = fulfilled, Reason = "Surgery", Priority = "High", Status = status,
                RejectionReason = rejectionReason, CreatedAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc), ExpiryDate = DateTime.UtcNow.AddDays(3)
            };
            await s.Context.BloodRequests.AddAsync(request);
            if (assignDoctor)
            {
                await s.Context.BloodRequestVerifications.AddAsync(new BloodRequestVerification { BloodRequestId = request.BloodRequestId, DoctorId = s.Doctor.DoctorId });
            }
            await s.Context.SaveChangesAsync();
            return request;
        }

        private static async Task<Acceptance> AddAcceptanceAsync(Seed s, BloodRequest request, AcceptanceStatus status, Guid? donorHospitalId = null,
            VerificationStatus? report = null)
        {
            var donor = new User { UserId = Guid.NewGuid(), FirstName = "Don", LastName = "Or", Email = $"{Guid.NewGuid():N}@h.org" };
            await s.Context.Users.AddAsync(donor);
            var acceptance = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = donor.UserId, DonorHospitalId = donorHospitalId, Status = status };
            await s.Context.Acceptances.AddAsync(acceptance);
            if (report.HasValue)
            {
                await s.Context.DonorVerifications.AddAsync(new DonorVerification { AcceptanceId = acceptance.AcceptanceId, ReportJson = "{}", Status = report.Value });
            }
            await s.Context.SaveChangesAsync();
            return acceptance;
        }

        private static async Task<BloodRequest> ReloadAsync(Seed s, BloodRequest r) =>
            await s.Context.BloodRequests.AsNoTracking().SingleAsync(x => x.BloodRequestId == r.BloodRequestId);

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
            Assert.Equal(BloodRequestStatus.Approved, (await ReloadAsync(s, request)).Status);

            // A hospital-created request: the hospital is its creator
            var own = await AddRequestAsync(s, BloodRequestStatus.Verified, creator: s.Staff.UserId);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Service.DeleteRequestAsync(own.BloodRequestId, s.Patient.UserId));
            await s.Service.DeleteRequestAsync(own.BloodRequestId, s.Staff.UserId);
            Assert.Equal(BloodRequestStatus.Deleted, (await ReloadAsync(s, own)).Status);
        }

        public static IEnumerable<object?[]> AllStates() => new List<object?[]>
        {
            new object?[] { BloodRequestStatus.Pending, null, false, 0 },
            new object?[] { BloodRequestStatus.Verified, null, true, 0 },
            new object?[] { BloodRequestStatus.Rejected, "Rejected by the hospital", false, 0 },
            new object?[] { BloodRequestStatus.Approved, null, true, 0 },
            new object?[] { BloodRequestStatus.Approved, null, true, 1 },   // partly donated
            new object?[] { BloodRequestStatus.Rejected, "Rejected by the doctor", true, 0 },
            new object?[] { BloodRequestStatus.Rejected, BloodRequestService.ExpiryRejectionReason, true, 0 },
            new object?[] { BloodRequestStatus.Cancelled, null, true, 0 },
            new object?[] { BloodRequestStatus.Completed, null, true, 2 },
        };

        [Theory]
        [MemberData(nameof(AllStates))]
        public async Task The_Creator_Can_Delete_In_Every_Status_And_Nothing_Is_Removed(BloodRequestStatus status, string? reason, bool assigned, int fulfilled)
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, status, rejectionReason: reason, assignDoctor: assigned, fulfilled: fulfilled);

            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            var row = await ReloadAsync(s, request);
            Assert.Equal(BloodRequestStatus.Deleted, row.Status);
            Assert.NotNull(row.DeletedAt);
            Assert.Equal(fulfilled, row.FulfilledUnits);
            Assert.Equal(assigned ? 1 : 0, s.Context.BloodRequestVerifications.Count(v => v.BloodRequestId == request.BloodRequestId));
        }

        [Fact]
        public async Task Deleting_Twice_Is_Refused()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Pending);
            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId));
        }

        [Theory]
        [InlineData(AcceptanceStatus.Accepted)]
        [InlineData(AcceptanceStatus.ScreeningPending)]
        [InlineData(AcceptanceStatus.ScreeningCompleted)]
        [InlineData(AcceptanceStatus.Verified)]
        public async Task Active_Acceptances_Are_Closed_And_Screening_Reports_Kept(AcceptanceStatus status)
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved, reserved: status == AcceptanceStatus.Verified ? 1 : 0);
            var report = status == AcceptanceStatus.Verified ? VerificationStatus.Approved
                : status == AcceptanceStatus.ScreeningCompleted ? VerificationStatus.Pending : (VerificationStatus?)null;
            var acceptance = await AddAcceptanceAsync(s, request, status, report: report);

            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            var closed = await s.Context.Acceptances.AsNoTracking().SingleAsync(a => a.AcceptanceId == acceptance.AcceptanceId);
            Assert.Equal(AcceptanceStatus.Cancelled, closed.Status);
            Assert.Equal(BloodRequestService.AcceptanceClosedByDeleteReason, closed.RejectionReason);
            Assert.NotNull(closed.CancelledAt);
            Assert.Equal(0, (await ReloadAsync(s, request)).ReservedUnits); // a reserved slot is freed

            // Screening reports are medical records: kept; a version still waiting for the doctor is closed
            var reports = s.Context.DonorVerifications.Where(v => v.AcceptanceId == acceptance.AcceptanceId).ToList();
            Assert.Equal(report.HasValue ? 1 : 0, reports.Count);
            if (report == VerificationStatus.Pending)
            {
                Assert.Equal(VerificationStatus.Closed, reports.Single().Status);
            }
            if (report == VerificationStatus.Approved)
            {
                Assert.Equal(VerificationStatus.Approved, reports.Single().Status); // decision history unchanged
            }
        }

        [Theory]
        [InlineData(AcceptanceStatus.Cancelled)]
        [InlineData(AcceptanceStatus.Rejected)]
        public async Task A_Screened_Donor_No_Longer_Blocks_Deletion(AcceptanceStatus status)
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved);
            var acceptance = await AddAcceptanceAsync(s, request, status, report: VerificationStatus.Rejected);

            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            Assert.Equal(BloodRequestStatus.Deleted, (await ReloadAsync(s, request)).Status);
            Assert.Equal(status, (await s.Context.Acceptances.FindAsync(acceptance.AcceptanceId))!.Status); // already closed: unchanged
            Assert.Single(s.Context.DonorVerifications.Where(v => v.AcceptanceId == acceptance.AcceptanceId));
        }

        [Fact]
        public async Task A_Donated_Acceptance_Stays_Matched()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved, fulfilled: 1);
            var donated = await AddAcceptanceAsync(s, request, AcceptanceStatus.Matched);

            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            Assert.Equal(AcceptanceStatus.Matched, (await s.Context.Acceptances.FindAsync(donated.AcceptanceId))!.Status);
            Assert.DoesNotContain(s.Context.Notifications, n => n.UserId == donated.DonorUserId);
        }

        [Fact]
        public async Task Partial_Fulfilment_Is_Preserved_While_Only_Active_Participation_Is_Closed_And_Notified()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved, reserved: 0, fulfilled: 2);
            request.UnitsRequired = 4;
            await s.Context.SaveChangesAsync();
            var completedA = await AddAcceptanceAsync(s, request, AcceptanceStatus.Matched);
            var completedB = await AddAcceptanceAsync(s, request, AcceptanceStatus.Matched);
            var active = await AddAcceptanceAsync(s, request, AcceptanceStatus.ScreeningCompleted, report: VerificationStatus.Pending);

            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            var saved = await ReloadAsync(s, request);
            Assert.Equal(BloodRequestStatus.Deleted, saved.Status);
            Assert.Equal(2, saved.FulfilledUnits);
            Assert.Equal(AcceptanceStatus.Matched, (await s.Context.Acceptances.FindAsync(completedA.AcceptanceId))!.Status);
            Assert.Equal(AcceptanceStatus.Matched, (await s.Context.Acceptances.FindAsync(completedB.AcceptanceId))!.Status);
            Assert.Equal(AcceptanceStatus.Cancelled, (await s.Context.Acceptances.FindAsync(active.AcceptanceId))!.Status);
            Assert.Equal(VerificationStatus.Closed, (await s.Context.DonorVerifications.SingleAsync(v => v.AcceptanceId == active.AcceptanceId)).Status);
            var deletionNotices = await s.Context.Notifications.Where(n => n.NotificationType == "BloodRequestDeleted").ToListAsync();
            Assert.Contains(deletionNotices, n => n.UserId == active.DonorUserId);
            Assert.DoesNotContain(deletionNotices, n => n.UserId == completedA.DonorUserId || n.UserId == completedB.DonorUserId);
            Assert.Contains(deletionNotices, n => n.HospitalId == s.Hospital.HospitalId);
            Assert.Contains(deletionNotices, n => n.UserId == s.DoctorLogin.UserId);
        }

        [Fact]
        public async Task Notifications_Go_To_The_Hospital_The_Doctor_And_The_Closed_Donors_And_Hospitals()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved);
            var activeDonor = await AddAcceptanceAsync(s, request, AcceptanceStatus.ScreeningPending);
            var withdrawn = await AddAcceptanceAsync(s, request, AcceptanceStatus.Cancelled);
            var hospitalOffer = await AddAcceptanceAsync(s, request, AcceptanceStatus.Accepted, donorHospitalId: s.OtherHospital.HospitalId);
            var unrelated = await AddRequestAsync(s, BloodRequestStatus.Approved, creator: s.Other.UserId);
            var unrelatedAcceptance = await AddAcceptanceAsync(s, unrelated, AcceptanceStatus.Accepted);

            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            var sent = s.Context.Notifications.Where(n => n.NotificationType == "BloodRequestDeleted").ToList();
            Assert.Equal(4, sent.Count);
            Assert.Single(sent, n => n.HospitalId == s.Hospital.HospitalId && n.UserId == null);           // hospital it was sent to
            Assert.Single(sent, n => n.UserId == s.DoctorLogin.UserId && n.RecipientRole == "Doctor");    // assigned doctor
            Assert.Single(sent, n => n.UserId == activeDonor.DonorUserId && n.RecipientRole == "Donor");   // closed donor acceptance
            Assert.Single(sent, n => n.HospitalId == s.OtherHospital.HospitalId);                         // closed hospital donation
            Assert.DoesNotContain(sent, n => n.UserId == withdrawn.DonorUserId);                           // had already withdrawn
            Assert.DoesNotContain(sent, n => n.UserId == s.Patient.UserId);                                // not the creator
            Assert.All(sent, n => Assert.Contains("Blood request for B+, 2 unit(s) at Venus Hospital (created 20 Sep 2026) was deleted by the requester.", n.Message));

            // Nothing else is touched
            Assert.Equal(BloodRequestStatus.Approved, (await ReloadAsync(s, unrelated)).Status);
            Assert.Equal(AcceptanceStatus.Accepted, (await s.Context.Acceptances.FindAsync(unrelatedAcceptance.AcceptanceId))!.Status);
            Assert.Equal(AcceptanceStatus.Cancelled, (await s.Context.Acceptances.FindAsync(hospitalOffer.AcceptanceId))!.Status);
        }

        [Fact]
        public async Task A_Removed_Or_Inactive_Doctor_Is_Not_Notified()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Verified);
            s.Doctor.IsActive = false;
            s.Doctor.DeletedAt = DateTime.UtcNow;
            await s.Context.SaveChangesAsync();

            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            Assert.DoesNotContain(s.Context.Notifications, n => n.UserId == s.DoctorLogin.UserId);
        }

        [Fact]
        public async Task Deleted_Requests_Are_Hidden_From_Creator_Hospital_And_Doctor_Lists()
        {
            var s = await SeedAsync();
            var deleted = await AddRequestAsync(s, BloodRequestStatus.Verified);
            var kept = await AddRequestAsync(s, BloodRequestStatus.Verified);

            await s.Service.DeleteRequestAsync(deleted.BloodRequestId, s.Patient.UserId);

            Assert.Equal(new[] { kept.BloodRequestId }, (await s.Service.GetMyRequestsAsync(s.Patient.UserId)).Select(r => r.BloodRequestId));
            Assert.Equal(new[] { kept.BloodRequestId }, (await s.Service.GetHospitalRequestsAsync(s.Hospital.HospitalId)).Select(r => r.BloodRequestId));
            Assert.Equal(new[] { kept.BloodRequestId }, (await s.Service.GetAssignedRequestsAsync(s.DoctorLogin.UserId)).Select(r => r.BloodRequestId));
            // The row itself is still there (the Admin still sees it)
            Assert.Equal("Deleted", (await s.Service.GetRequestByIdAsync(deleted.BloodRequestId))!.Status);
        }

        [Fact]
        public async Task The_Donor_Sees_The_Closed_Acceptance_Without_The_Deleted_Requests_Details()
        {
            var s = await SeedAsync();
            var request = await AddRequestAsync(s, BloodRequestStatus.Approved);
            var acceptance = await AddAcceptanceAsync(s, request, AcceptanceStatus.ScreeningPending);

            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);

            var mine = (await new AcceptanceService(s.Context, new BloodCompatibilityService()).GetMyAcceptancesAsync(acceptance.DonorUserId)).Single();
            Assert.True(mine.RequestDeleted);
            Assert.Equal("Cancelled", mine.Status);
            Assert.Equal(BloodRequestService.AcceptanceClosedByDeleteReason, mine.RejectionReason);
            Assert.Null(mine.HospitalName);
            Assert.Null(mine.RequestBloodGroup);
            Assert.Null(mine.RequestPriority);
            Assert.Equal(0, mine.UnitsRequired);
        }

        [Fact]
        public async Task The_One_Active_Request_Slot_Is_Freed()
        {
            var s = await SeedAsync();
            var dto = new CreateBloodRequestDto { HospitalId = s.Hospital.HospitalId, BloodGroup = "B+", UnitsRequired = 1, Reason = "Surgery", Priority = "High" };
            var first = await s.Service.CreateRequestAsync(s.Patient.UserId, dto);
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.CreateRequestAsync(s.Patient.UserId, dto)); // duplicate

            await s.Service.DeleteRequestAsync(first.BloodRequestId, s.Patient.UserId);

            var second = await s.Service.CreateRequestAsync(s.Patient.UserId, dto);
            Assert.NotEqual(first.BloodRequestId, second.BloodRequestId);
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

            // The delete wins; the later accept is refused and leaves nothing behind (an accept racing the delete fails on
            // the request's concurrency token instead)
            await s.Service.DeleteRequestAsync(request.BloodRequestId, s.Patient.UserId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new AcceptanceService(s.Context, new BloodCompatibilityService())
                .AcceptRequestAsync(donor.UserId, new CreateAcceptanceDto { BloodRequestId = request.BloodRequestId, DonorBloodGroup = "B+" }));
            Assert.Empty(s.Context.Acceptances.Where(a => a.BloodRequestId == request.BloodRequestId));
        }

        [Fact]
        public async Task The_Assistant_Snapshot_Never_Lists_Deleted_Requests()
        {
            var s = await SeedAsync();
            await AddRequestAsync(s, BloodRequestStatus.Deleted);
            await AddRequestAsync(s, BloodRequestStatus.Pending);

            var snapshot = await new AssistantContextBuilder(s.Context).BuildAsync(s.Patient, "User", s.Patient.Email);

            var requests = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(snapshot["requests"]).ToList();
            Assert.Single(requests);
            Assert.Equal("Pending", requests[0]["status"]);
        }
    }
}
