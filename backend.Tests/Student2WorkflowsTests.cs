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
            var doctorService = new DoctorService(context);

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
                PhoneNumber = "555-9999",
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

            // Seed active donors
            var donor1 = new User { UserId = Guid.NewGuid(), FirstName = "Alice", LastName = "Smith", Email = "alice@example.com", AccountStatus = AccountStatus.Active };
            var donor2 = new User { UserId = Guid.NewGuid(), FirstName = "Bob", LastName = "Jones", Email = "bob@example.com", AccountStatus = AccountStatus.Active };
            await context.Users.AddRangeAsync(donor1, donor2);

            // Seed hospital and doctor
            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "St. Jude", IsVerified = true };
            var doctor = new Doctor { DoctorId = Guid.NewGuid(), HospitalId = hospital.HospitalId, FirstName = "John", LastName = "Watson", Email = "watson@stjude.org" };
            await context.Hospitals.AddAsync(hospital);
            await context.Doctors.AddAsync(doctor);
            await context.SaveChangesAsync();

            var requestId = Guid.NewGuid();
            var approveDto = new ApproveRejectRequestDto
            {
                DoctorId = doctor.DoctorId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                Priority = "Normal",
                Notes = "Patient verified and cleared for matching."
            };

            var result = await verificationService.ApproveBloodRequestAsync(requestId, approveDto);
            Assert.NotNull(result);
            Assert.Equal("Approved", result.Status);

            // Verify notifications
            var notifications = await context.Notifications.ToListAsync();
            Assert.Equal(2, notifications.Count);
            Assert.All(notifications, n => Assert.Equal("EligibleDonorAlert", n.NotificationType));
        }

        [Fact]
        public async Task High_Or_Critical_Priority_Approval_Notifies_Donors_And_UrgentHospitals_Only()
        {
            var context = GetInMemoryDbContext();
            var notificationService = CreateNotificationService(context);
            var verificationService = new VerificationService(context, notificationService);

            // Seed role and Admin user
            var adminRole = new Role { RoleId = 10, Name = "Admin" };
            var adminUser = new User { UserId = Guid.NewGuid(), FirstName = "Super", LastName = "Admin", Email = "admin@lifelink.org", AccountStatus = AccountStatus.Active };
            var userRole = new UserRole { UserId = adminUser.UserId, RoleId = adminRole.RoleId, Role = adminRole, User = adminUser };
            adminUser.UserRoles.Add(userRole);

            var donorUser = new User { UserId = Guid.NewGuid(), FirstName = "Donor", LastName = "One", Email = "donor@lifelink.org", AccountStatus = AccountStatus.Active };

            // Seed hospitals
            var requestingHospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Origin Hospital", IsVerified = true };
            var otherHospital1 = new Hospital { HospitalId = Guid.NewGuid(), Name = "Backup Hospital 1", IsVerified = true };
            var otherHospital2 = new Hospital { HospitalId = Guid.NewGuid(), Name = "Backup Hospital 2", IsVerified = true };
            var unverifiedHospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Pending Hospital", IsVerified = false };

            var doctor = new Doctor { DoctorId = Guid.NewGuid(), HospitalId = requestingHospital.HospitalId, FirstName = "Stephen", LastName = "Strange", Email = "strange@origin.org" };

            await context.Roles.AddAsync(adminRole);
            await context.Users.AddRangeAsync(adminUser, donorUser);
            await context.Hospitals.AddRangeAsync(requestingHospital, otherHospital1, otherHospital2, unverifiedHospital);
            await context.Doctors.AddAsync(doctor);
            await context.SaveChangesAsync();

            var requestId = Guid.NewGuid();
            var approveDto = new ApproveRejectRequestDto
            {
                DoctorId = doctor.DoctorId,
                HospitalId = requestingHospital.HospitalId,
                BloodGroup = "O-",
                Priority = "Critical",
                Notes = "CRITICAL: Urgent emergency blood requirement!"
            };

            var result = await verificationService.ApproveBloodRequestAsync(requestId, approveDto);
            Assert.NotNull(result);
            Assert.Equal("Approved", result.Status);

            var notifications = await context.Notifications.ToListAsync();
            // Donors: 2 (adminUser + donorUser), Urgent Hospitals: 2 (otherHospital1 + otherHospital2), Admins: 0 => Total: 4
            Assert.Equal(4, notifications.Count);

            Assert.Equal(2, notifications.Count(n => n.NotificationType == "EligibleDonorAlert"));
            Assert.Equal(2, notifications.Count(n => n.NotificationType == "UrgentHospitalAlert"));
            Assert.Empty(notifications.Where(n => n.NotificationType == "AdminUrgentAlert"));
            Assert.Empty(notifications.Where(n => n.RecipientRole == "Admin"));
        }

        [Fact]
        public async Task Donor_Verification_Review_Workflow()
        {
            var context = GetInMemoryDbContext();
            var notificationService = CreateNotificationService(context);
            var verificationService = new VerificationService(context, notificationService);

            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Mercy Hospital" };
            var doctor = new Doctor { DoctorId = Guid.NewGuid(), HospitalId = hospital.HospitalId, FirstName = "Meredith", LastName = "Grey", Email = "grey@mercy.org" };
            await context.Hospitals.AddAsync(hospital);
            await context.Doctors.AddAsync(doctor);
            await context.SaveChangesAsync();

            var acceptanceId = Guid.NewGuid();

            // Approve donor medical report
            var approved = await verificationService.ApproveDonorVerificationAsync(acceptanceId, new ApproveRejectRequestDto
            {
                DoctorId = doctor.DoctorId,
                MedicalReportSummary = "Hemoglobin 14.5, No infectious diseases, eligible for donation.",
                Notes = "Cleared for match."
            });

            Assert.NotNull(approved);
            Assert.Equal("Approved", approved.Status);
            Assert.Equal(acceptanceId, approved.AcceptanceId);

            // Reject scenario
            var rejectedId = Guid.NewGuid();
            var rejected = await verificationService.RejectDonorVerificationAsync(rejectedId, new ApproveRejectRequestDto
            {
                DoctorId = doctor.DoctorId,
                MedicalReportSummary = "Low platelet count.",
                Notes = "Ineligible at this time."
            });

            Assert.NotNull(rejected);
            Assert.Equal("Rejected", rejected.Status);
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
