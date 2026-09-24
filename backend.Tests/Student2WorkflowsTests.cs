using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Doctors;
using LifeLink.DTOs.Hospitals;
using LifeLink.DTOs.Matching;
using LifeLink.DTOs.Verification;
using LifeLink.Entities;
using LifeLink.Services.Doctors;
using LifeLink.Services.Hospitals;
using LifeLink.Services.Matching;
using LifeLink.Services.Notification;
using LifeLink.Services.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using Xunit;

namespace LifeLink.Tests
{
    public class Student2WorkflowsTests
    {
        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        [Fact]
        public async Task Hospital_Registration_And_Admin_Verification_Workflow()
        {
            var context = GetInMemoryDbContext();
            var service = new HospitalService(context);

            var createDto = new CreateHospitalDto
            {
                Name = "City General Hospital",
                LicenseNumber = "HOSP-12345",
                Address = "123 Healthcare Ave",
                ContactNumber = "555-0199",
                Email = "contact@citygeneral.org"
            };

            var created = await service.CreateHospitalAsync(createDto);
            Assert.NotNull(created);
            Assert.False(created.IsVerified);
            Assert.Equal("City General Hospital", created.Name);

            // Admin verifies hospital
            var verified = await service.VerifyHospitalAsync(created.HospitalId, true);
            Assert.NotNull(verified);
            Assert.True(verified.IsVerified);

            var retrieved = await service.GetHospitalByIdAsync(created.HospitalId);
            Assert.NotNull(retrieved);
            Assert.True(retrieved.IsVerified);
        }

        [Fact]
        public async Task Doctor_Creation_And_Retrieval_Workflow()
        {
            var context = GetInMemoryDbContext();
            var hospitalService = new HospitalService(context);
            var doctorService = new DoctorService(context, new LifeLink.Services.Auth.PasswordHasherService());

            var hospital = await hospitalService.CreateHospitalAsync(new CreateHospitalDto
            {
                Name = "Apex Hospital"
            });

            var doctorDto = new CreateDoctorDto
            {
                HospitalId = hospital.HospitalId,
                FirstName = "Gregory",
                LastName = "House",
                Email = "house@apex.org",
                Password = "Initial#Pass1",
                PhoneNumber = "5559999000",
                LicenseNumber = "DOC-9876",
                Specialization = "Diagnostic Medicine"
            };

            var doctor = await doctorService.CreateDoctorAsync(doctorDto);
            Assert.NotNull(doctor);
            Assert.Equal("Gregory", doctor.FirstName);
            Assert.Equal(hospital.HospitalId, doctor.HospitalId);

            var list = await doctorService.GetDoctorsAsync(hospital.HospitalId);
            Assert.Single(list);
            Assert.Equal(doctor.DoctorId, list.First().DoctorId);
        }

        // A request the hospital has verified and assigned to the given doctor (awaiting the doctor's decision)
        private static async Task<Guid> SeedVerifiedRequestAsync(AppDbContext context, Guid hospitalId, Guid doctorId, string bloodGroup, string priority)
        {
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = Guid.NewGuid(),
                HospitalId = hospitalId,
                BloodGroup = bloodGroup,
                UnitsRequired = 2,
                Reason = "Surgery",
                Priority = priority,
                Status = BloodRequestStatus.Verified,
                ExpiryDate = DateTime.UtcNow.AddDays(7)
            };
            await context.BloodRequests.AddAsync(request);
            await context.BloodRequestVerifications.AddAsync(new BloodRequestVerification
            {
                BloodRequestId = request.BloodRequestId,
                DoctorId = doctorId,
                Status = VerificationStatus.Pending
            });
            await context.SaveChangesAsync();
            return request.BloodRequestId;
        }

        private NotificationAgentService CreateNotificationService(AppDbContext context)
        {
            var config = new ConfigurationBuilder().Build();
            var logger = NullLogger<NotificationAgentService>.Instance;
            return new NotificationAgentService(context, new HttpClient(), config, logger);
        }

        [Fact]
        public async Task Normal_Priority_Approval_Notifies_Only_Eligible_Donors()
        {
            var context = GetInMemoryDbContext();
            var notificationService = CreateNotificationService(context);
            var verificationService = new VerificationService(context, notificationService);

            // Eligible: known compatible group, no recent donation. Not eligible: incompatible, donated 30 days ago, unknown group
            var donor1 = new User { UserId = Guid.NewGuid(), FirstName = "Alice", LastName = "Smith", Email = "alice@example.com", AccountStatus = AccountStatus.Active, BloodGroup = "A+" };
            var donor2 = new User { UserId = Guid.NewGuid(), FirstName = "Bob", LastName = "Jones", Email = "bob@example.com", AccountStatus = AccountStatus.Active, BloodGroup = "O-" };
            var incompatible = new User { UserId = Guid.NewGuid(), FirstName = "Cara", LastName = "B", Email = "cara@example.com", AccountStatus = AccountStatus.Active, BloodGroup = "B+" };
            var recent = new User { UserId = Guid.NewGuid(), FirstName = "Dan", LastName = "R", Email = "dan@example.com", AccountStatus = AccountStatus.Active, BloodGroup = "A+", LastDonationDate = DateTime.UtcNow.AddDays(-30) };
            var unknown = new User { UserId = Guid.NewGuid(), FirstName = "Eve", LastName = "U", Email = "eve@example.com", AccountStatus = AccountStatus.Active };
            await context.Users.AddRangeAsync(donor1, donor2, incompatible, recent, unknown);

            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "St. Jude", IsVerified = true };
            // Doctor login id without a Users row so the doctor is not counted as a donor recipient
            var doctor = new Doctor { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = hospital.HospitalId, FirstName = "John", LastName = "Watson", Email = "watson@stjude.org" };
            await context.Hospitals.AddAsync(hospital);
            await context.Doctors.AddAsync(doctor);
            await context.SaveChangesAsync();

            var requestId = await SeedVerifiedRequestAsync(context, hospital.HospitalId, doctor.DoctorId, "A+", "Normal");

            var result = await verificationService.ApproveBloodRequestAsync(requestId, doctor.UserId!.Value, "Patient verified and cleared for matching.");
            Assert.NotNull(result);
            Assert.Equal("Approved", result.Status);
            Assert.Equal(BloodRequestStatus.Approved, (await context.BloodRequests.FindAsync(requestId))!.Status);

            var notifications = await context.Notifications.ToListAsync();
            Assert.Equal(2, notifications.Count);
            Assert.All(notifications, n => Assert.Equal("EligibleDonorAlert", n.NotificationType));
            Assert.All(notifications, n => Assert.Null(n.HospitalId)); // donor alerts never land in a hospital's notification centre
            Assert.Equal(new[] { donor1.UserId, donor2.UserId }.OrderBy(x => x), notifications.Select(n => n.UserId!.Value).OrderBy(x => x));
        }

        [Fact]
        public async Task High_Or_Critical_Priority_Approval_Notifies_Donors_And_UrgentHospitals_Only()
        {
            var context = GetInMemoryDbContext();
            var notificationService = CreateNotificationService(context);
            var verificationService = new VerificationService(context, notificationService);

            // Admin accounts never receive donor alerts, whatever their blood group
            var adminRole = new Role { RoleId = 10, Name = "Admin" };
            var adminUser = new User { UserId = Guid.NewGuid(), FirstName = "Super", LastName = "Admin", Email = "admin@lifelink.org", AccountStatus = AccountStatus.Active, BloodGroup = "O-" };
            var userRole = new UserRole { UserId = adminUser.UserId, RoleId = adminRole.RoleId, Role = adminRole, User = adminUser };
            adminUser.UserRoles.Add(userRole);

            var donorUser = new User { UserId = Guid.NewGuid(), FirstName = "Donor", LastName = "One", Email = "donor@lifelink.org", AccountStatus = AccountStatus.Active, BloodGroup = "O-" };
            var suspendedDonor = new User { UserId = Guid.NewGuid(), FirstName = "Sus", LastName = "Pended", Email = "s@lifelink.org", AccountStatus = AccountStatus.Suspended, IsSuspended = true, BloodGroup = "O-" };

            var requestingHospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Origin Hospital", IsVerified = true };
            var otherHospital1 = new Hospital { HospitalId = Guid.NewGuid(), Name = "Backup Hospital 1", IsVerified = true };
            var otherHospital2 = new Hospital { HospitalId = Guid.NewGuid(), Name = "Backup Hospital 2", IsVerified = true };
            var unverifiedHospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Pending Hospital", IsVerified = false };
            var suspendedHospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Suspended Hospital", IsVerified = true, IsSuspended = true };

            var doctor = new Doctor { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = requestingHospital.HospitalId, FirstName = "Stephen", LastName = "Strange", Email = "strange@origin.org" };

            await context.Roles.AddAsync(adminRole);
            await context.Users.AddRangeAsync(adminUser, donorUser, suspendedDonor);
            await context.Hospitals.AddRangeAsync(requestingHospital, otherHospital1, otherHospital2, unverifiedHospital, suspendedHospital);
            await context.Doctors.AddAsync(doctor);
            await context.SaveChangesAsync();

            var requestId = await SeedVerifiedRequestAsync(context, requestingHospital.HospitalId, doctor.DoctorId, "O-", "Critical");

            var result = await verificationService.ApproveBloodRequestAsync(requestId, doctor.UserId!.Value, "CRITICAL: Urgent emergency blood requirement!");
            Assert.NotNull(result);
            Assert.Equal("Approved", result.Status);

            var notifications = await context.Notifications.ToListAsync();
            // Donors: 1 (active donor only), Urgent Hospitals: 2 (approved, not suspended, not the requester)
            Assert.Equal(3, notifications.Count);
            Assert.Equal(donorUser.UserId, Assert.Single(notifications, n => n.NotificationType == "EligibleDonorAlert").UserId);
            Assert.Equal(2, notifications.Count(n => n.NotificationType == "UrgentHospitalAlert"));
            Assert.DoesNotContain(notifications, n => n.HospitalId == suspendedHospital.HospitalId || n.HospitalId == unverifiedHospital.HospitalId);
            Assert.Empty(notifications.Where(n => n.RecipientRole == "Admin"));
        }

        [Fact]
        public async Task Donor_Verification_Review_Workflow()
        {
            var context = GetInMemoryDbContext();
            var notificationService = CreateNotificationService(context);
            var verificationService = new VerificationService(context, notificationService);

            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Mercy Hospital", IsVerified = true };
            var doctor = new Doctor { DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = hospital.HospitalId, FirstName = "Meredith", LastName = "Grey", Email = "grey@mercy.org", IsActive = true };
            var donorA = new User { UserId = Guid.NewGuid(), FirstName = "A", LastName = "Donor", Email = "a@d.org", BloodGroup = "B+" };
            var donorB = new User { UserId = Guid.NewGuid(), FirstName = "B", LastName = "Donor", Email = "b@d.org", BloodGroup = "B+" };
            var request = new BloodRequest { BloodRequestId = Guid.NewGuid(), PatientUserId = Guid.NewGuid(), HospitalId = hospital.HospitalId, BloodGroup = "B+", UnitsRequired = 2, Reason = "Surgery", Priority = "Normal", Status = BloodRequestStatus.Approved, ExpiryDate = DateTime.UtcNow.AddDays(3) };
            var accA = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = donorA.UserId, Status = AcceptanceStatus.ScreeningCompleted };
            var accB = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = donorB.UserId, Status = AcceptanceStatus.ScreeningCompleted };
            var reportA = new DonorVerification { AcceptanceId = accA.AcceptanceId, DoctorId = doctor.DoctorId, ReportVersion = 1, ReportJson = "{\"risk_level\":\"LOW\"}" };
            var reportB = new DonorVerification { AcceptanceId = accB.AcceptanceId, DoctorId = doctor.DoctorId, ReportVersion = 1, ReportJson = "{\"risk_level\":\"HIGH\"}" };
            await context.Hospitals.AddAsync(hospital);
            await context.Doctors.AddAsync(doctor);
            await context.Users.AddRangeAsync(donorA, donorB);
            await context.BloodRequests.AddAsync(request);
            await context.Acceptances.AddRangeAsync(accA, accB);
            await context.DonorVerifications.AddRangeAsync(reportA, reportB);
            await context.SaveChangesAsync();

            // Approve: the doctor comes from the signed-in account; approval reserves a slot, fulfilment is unchanged
            var approved = await verificationService.ApproveDonorVerificationAsync(reportA.DonorVerificationId, doctor.UserId!.Value, "Cleared for match.");
            Assert.Equal("Approved", approved.Status);
            Assert.Equal("LOW", approved.RiskLevel);
            Assert.Equal(doctor.DoctorId, approved.DecidedByDoctorId);
            Assert.Equal(AcceptanceStatus.Verified, (await context.Acceptances.FindAsync(accA.AcceptanceId))!.Status);
            Assert.Equal(1, request.ReservedUnits);
            Assert.Equal(0, request.FulfilledUnits);

            // Reject requires a reason, which the donor sees
            await Assert.ThrowsAsync<InvalidOperationException>(() => verificationService.RejectDonorVerificationAsync(reportB.DonorVerificationId, doctor.UserId!.Value, " "));
            var rejected = await verificationService.RejectDonorVerificationAsync(reportB.DonorVerificationId, doctor.UserId!.Value, "Low haemoglobin.");
            Assert.Equal("Rejected", rejected.Status);
            Assert.Equal("Low haemoglobin.", (await context.Acceptances.FindAsync(accB.AcceptanceId))!.RejectionReason);
            Assert.Equal(BloodRequestStatus.Approved, request.Status); // still open to other donors
        }

        [Fact]
        public async Task Matching_Workflow_Creates_Match_And_Removes_From_Dashboard()
        {
            var context = GetInMemoryDbContext();
            var matchingService = new MatchingService(context);

            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Seattle Grace" };
            var doctor = new Doctor { DoctorId = Guid.NewGuid(), HospitalId = hospital.HospitalId, FirstName = "Derek", LastName = "Shepherd", Email = "shepherd@sg.org" };
            var donor = new User { UserId = Guid.NewGuid(), FirstName = "Mark", LastName = "Sloan", Email = "sloan@sg.org" };

            var emergencyReq = new EmergencyRequest
            {
                EmergencyRequestId = Guid.NewGuid(),
                HospitalId = hospital.HospitalId,
                BloodGroup = "B+",
                UnitsRequired = 2,
                Priority = "High",
                Status = EmergencyRequestStatus.Pending.ToString(),
                Reason = "Surgery"
            };

            await context.Hospitals.AddAsync(hospital);
            await context.Doctors.AddAsync(doctor);
            await context.Users.AddAsync(donor);
            await context.EmergencyRequests.AddAsync(emergencyReq);
            await context.SaveChangesAsync();

            var matchDto = new CreateMatchDto
            {
                BloodRequestId = emergencyReq.EmergencyRequestId,
                DonorUserId = donor.UserId,
                DoctorId = doctor.DoctorId,
                Notes = "Matched with cleared donor."
            };

            var match = await matchingService.CreateMatchAsync(matchDto);
            Assert.NotNull(match);
            Assert.Equal("Matched", match.Status);
            Assert.True(match.IsRemovedFromPublicDashboard);

            // Check emergency request status is Completed (removed from active dashboard)
            var updatedReq = await context.EmergencyRequests.FindAsync(emergencyReq.EmergencyRequestId);
            Assert.NotNull(updatedReq);
            Assert.Equal(EmergencyRequestStatus.Completed.ToString(), updatedReq.Status);

            var allMatches = await matchingService.GetMatchesAsync();
            Assert.Single(allMatches);
        }
    }
}
