using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.DTOs.BloodRequests;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using LifeLink.Services.BloodCompatibility;
using LifeLink.Services.BloodRequests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeLink.Tests
{
    public class Student1WorkflowsTests
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

        private async Task<(Hospital verifiedHospital, Hospital unverifiedHospital, User patientUser, User donorUser1, User donorUser2)> SeedBaseDataAsync(AppDbContext context)
        {
            var verifiedHospital = new Hospital
            {
                HospitalId = Guid.NewGuid(),
                Name = "Jaffna Teaching Hospital",
                IsVerified = true
            };

            var unverifiedHospital = new Hospital
            {
                HospitalId = Guid.NewGuid(),
                Name = "Unverified Clinic",
                IsVerified = false
            };

            var patientUser = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = "Alice",
                LastName = "Smith",
                Email = "alice@lifelink.org"
            };

            var donorUser1 = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = "Bob",
                LastName = "Jones",
                Email = "bob@lifelink.org"
            };

            var donorUser2 = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = "Charlie",
                LastName = "Brown",
                Email = "charlie@lifelink.org"
            };

            await context.Hospitals.AddRangeAsync(verifiedHospital, unverifiedHospital);
            await context.Users.AddRangeAsync(patientUser, donorUser1, donorUser2);

            // Mark donorUser1 and donorUser2 as verified donors
            var doctor = new Doctor
            {
                DoctorId = Guid.NewGuid(),
                HospitalId = verifiedHospital.HospitalId,
                FirstName = "Gregory",
                LastName = "House",
                Email = "house@jth.org"
            };
            await context.Doctors.AddAsync(doctor);

            var verification1 = new DonorVerification
            {
                DonorVerificationId = Guid.NewGuid(),
                AcceptanceId = donorUser1.UserId,
                DoctorId = doctor.DoctorId,
                Status = VerificationStatus.Approved
            };

            var verification2 = new DonorVerification
            {
                DonorVerificationId = Guid.NewGuid(),
                AcceptanceId = donorUser2.UserId,
                DoctorId = doctor.DoctorId,
                Status = VerificationStatus.Approved
            };

            await context.DonorVerifications.AddRangeAsync(verification1, verification2);
            await context.SaveChangesAsync();

            return (verifiedHospital, unverifiedHospital, patientUser, donorUser1, donorUser2);
        }

        // =========================================================================
        // GROUP A: Blood Request Creation & Validation
        // =========================================================================

        [Fact]
        public async Task Create_Blood_Request_Successfully()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var dto = new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 2,
                Reason = "Surgery scheduled",
                Priority = "High"
            };

            var result = await service.CreateRequestAsync(patient.UserId, dto);

            Assert.NotNull(result);
            Assert.Equal("Pending", result.Status);
            Assert.Equal(0, result.FulfilledUnits);
            Assert.Equal("A+", result.BloodGroup);
            Assert.Equal("High", result.Priority);
            Assert.True(result.ExpiryDate > DateTime.UtcNow.AddDays(6));
        }

        [Fact]
        public async Task Invalid_Blood_Group_Rejected()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var dto = new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "XYZ",
                UnitsRequired = 2,
                Reason = "Need blood",
                Priority = "Normal"
            };

            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRequestAsync(patient.UserId, dto));
        }

        [Fact]
        public async Task Invalid_Priority_Rejected()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var dto = new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "O+",
                UnitsRequired = 2,
                Reason = "Need blood",
                Priority = "SuperUrgent"
            };

            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRequestAsync(patient.UserId, dto));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(11)]
        public async Task Invalid_Units_Rejected(int units)
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var dto = new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "O+",
                UnitsRequired = units,
                Reason = "Need blood",
                Priority = "Normal"
            };

            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRequestAsync(patient.UserId, dto));
        }

        [Fact]
        public async Task Hospital_Required_Validation()
        {
            var context = GetInMemoryDbContext();
            var (_, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var dto = new CreateBloodRequestDto
            {
                HospitalId = Guid.NewGuid(), // Non-existent hospital
                BloodGroup = "B+",
                UnitsRequired = 1,
                Reason = "Need blood",
                Priority = "Normal"
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(patient.UserId, dto));
        }

        [Fact]
        public async Task Verified_Hospital_Validation_Enforced()
        {
            var context = GetInMemoryDbContext();
            var (_, unverifiedHospital, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var dto = new CreateBloodRequestDto
            {
                HospitalId = unverifiedHospital.HospitalId,
                BloodGroup = "B+",
                UnitsRequired = 1,
                Reason = "Need blood",
                Priority = "Normal"
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(patient.UserId, dto));
            Assert.Contains("verified", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // =========================================================================
        // GROUP B: Duplicate Active Request Prevention
        // =========================================================================

        [Fact]
        public async Task Duplicate_Pending_Request_Blocked()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var dto1 = new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Reason = "First request",
                Priority = "Normal"
            };
            await service.CreateRequestAsync(patient.UserId, dto1);

            // Attempt duplicate
            var dto2 = new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 2,
                Reason = "Second request duplicate",
                Priority = "High"
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(patient.UserId, dto2));
            Assert.Contains("active request already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Duplicate_Approved_Request_Blocked()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var existing = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "O+",
                UnitsRequired = 2,
                Status = BloodRequestStatus.Approved,
                Reason = "Approved request",
                Priority = "Normal",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(7)
            };
            await context.BloodRequests.AddAsync(existing);
            await context.SaveChangesAsync();

            var dto = new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "O+",
                UnitsRequired = 1,
                Reason = "Another O+ request",
                Priority = "Normal"
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(patient.UserId, dto));
            Assert.Contains("active request already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Completed_Request_Allows_New_Request_Creation()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var completed = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 2,
                FulfilledUnits = 2,
                Status = BloodRequestStatus.Completed,
                Reason = "Past completed",
                Priority = "Normal",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                UpdatedAt = DateTime.UtcNow.AddDays(-5),
                ExpiryDate = DateTime.UtcNow.AddDays(-3)
            };
            await context.BloodRequests.AddAsync(completed);
            await context.SaveChangesAsync();

            var dto = new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Reason = "New request after completed",
                Priority = "Normal"
            };

            var result = await service.CreateRequestAsync(patient.UserId, dto);
            Assert.NotNull(result);
            Assert.Equal("Pending", result.Status);
        }

        [Fact]
        public async Task Cancelled_Request_Allows_New_Request_Creation()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var cancelled = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "B-",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Cancelled,
                CancelledAt = DateTime.UtcNow.AddDays(-1),
                Reason = "Old cancelled",
                Priority = "Normal",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                UpdatedAt = DateTime.UtcNow.AddDays(-1),
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(cancelled);
            await context.SaveChangesAsync();

            var dto = new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "B-",
                UnitsRequired = 1,
                Reason = "New request after cancelled",
                Priority = "Normal"
            };

            var result = await service.CreateRequestAsync(patient.UserId, dto);
            Assert.NotNull(result);
            Assert.Equal("Pending", result.Status);
        }

        [Fact]
        public async Task Different_Hospital_Allows_Creation()
        {
            var context = GetInMemoryDbContext();
            var (hospital1, _, patient, _, _) = await SeedBaseDataAsync(context);
            var hospital2 = new Hospital { HospitalId = Guid.NewGuid(), Name = "Colombo National Hospital", IsVerified = true };
            await context.Hospitals.AddAsync(hospital2);
            await context.SaveChangesAsync();

            var service = new BloodRequestService(context);

            var dto1 = new CreateBloodRequestDto
            {
                HospitalId = hospital1.HospitalId,
                BloodGroup = "O+",
                UnitsRequired = 1,
                Reason = "Req 1",
                Priority = "Normal"
            };
            await service.CreateRequestAsync(patient.UserId, dto1);

            var dto2 = new CreateBloodRequestDto
            {
                HospitalId = hospital2.HospitalId,
                BloodGroup = "O+",
                UnitsRequired = 1,
                Reason = "Req 2 at different hospital",
                Priority = "Normal"
            };

            var result = await service.CreateRequestAsync(patient.UserId, dto2);
            Assert.NotNull(result);
            Assert.Equal(hospital2.HospitalId, result.HospitalId);
        }

        [Fact]
        public async Task Different_Blood_Group_Allows_Creation()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var dto1 = new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Reason = "Req 1",
                Priority = "Normal"
            };
            await service.CreateRequestAsync(patient.UserId, dto1);

            var dto2 = new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "B+",
                UnitsRequired = 1,
                Reason = "Req 2 with different group",
                Priority = "Normal"
            };

            var result = await service.CreateRequestAsync(patient.UserId, dto2);
            Assert.NotNull(result);
            Assert.Equal("B+", result.BloodGroup);
        }

        // =========================================================================
        // GROUP C: Request Lifecycle & Public Feed
        // =========================================================================

        [Fact]
        public async Task Request_Cancellation_Works()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var created = await service.CreateRequestAsync(patient.UserId, new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "AB+",
                UnitsRequired = 2,
                Reason = "Needs blood",
                Priority = "Normal"
            });

            var cancelled = await service.CancelRequestAsync(created.BloodRequestId, patient.UserId);
            Assert.Equal("Cancelled", cancelled.Status);
            Assert.NotNull(cancelled.CancelledAt);
        }

        [Fact]
        public async Task Public_Feed_Excludes_Expired_Requests()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var expired = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "O+",
                UnitsRequired = 2,
                Status = BloodRequestStatus.Approved,
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                UpdatedAt = DateTime.UtcNow.AddDays(-10),
                ExpiryDate = DateTime.UtcNow.AddDays(-3),
                Reason = "Expired req",
                Priority = "Normal"
            };
            await context.BloodRequests.AddAsync(expired);
            await context.SaveChangesAsync();

            var publicFeed = await service.GetPublicRequestsAsync();
            Assert.DoesNotContain(publicFeed, r => r.BloodRequestId == expired.BloodRequestId);
        }

        [Fact]
        public async Task Public_Feed_Excludes_Cancelled_Requests()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var cancelled = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "O+",
                UnitsRequired = 2,
                Status = BloodRequestStatus.Cancelled,
                CancelledAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(7),
                Reason = "Cancelled req",
                Priority = "Normal"
            };
            await context.BloodRequests.AddAsync(cancelled);
            await context.SaveChangesAsync();

            var publicFeed = await service.GetPublicRequestsAsync();
            Assert.DoesNotContain(publicFeed, r => r.BloodRequestId == cancelled.BloodRequestId);
        }

        [Fact]
        public async Task Public_Feed_Excludes_Completed_Requests()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var service = new BloodRequestService(context);

            var completed = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "O+",
                UnitsRequired = 2,
                FulfilledUnits = 2,
                Status = BloodRequestStatus.Completed,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(7),
                Reason = "Completed req",
                Priority = "Normal"
            };
            await context.BloodRequests.AddAsync(completed);
            await context.SaveChangesAsync();

            var publicFeed = await service.GetPublicRequestsAsync();
            Assert.DoesNotContain(publicFeed, r => r.BloodRequestId == completed.BloodRequestId);
        }

        // =========================================================================
        // GROUP D: Blood Compatibility Matrix
        // =========================================================================

        [Fact]
        public void BloodCompatibility_O_Negative_To_A_Positive_Allowed()
        {
            var service = new BloodCompatibilityService();
            Assert.True(service.IsCompatible("O-", "A+"));
        }

        [Fact]
        public void BloodCompatibility_O_Negative_To_AB_Positive_Allowed()
        {
            var service = new BloodCompatibilityService();
            Assert.True(service.IsCompatible("O-", "AB+"));
        }

        [Fact]
        public void BloodCompatibility_A_Positive_To_O_Positive_Blocked()
        {
            var service = new BloodCompatibilityService();
            Assert.False(service.IsCompatible("A+", "O+"));
        }

        [Fact]
        public void BloodCompatibility_AB_Positive_To_O_Negative_Blocked()
        {
            var service = new BloodCompatibilityService();
            Assert.False(service.IsCompatible("AB+", "O-"));
        }

        // =========================================================================
        // GROUP E: Donor Acceptance & Multi-Donor Oversubscription
        // =========================================================================

        [Fact]
        public async Task Donor_Cannot_Accept_Own_Request()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "O+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.AcceptRequestAsync(patient.UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = request.BloodRequestId,
                    DonorBloodGroup = "O+"
                }));

            Assert.Contains("own", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Duplicate_Acceptance_Blocked()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 2,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "A+"
            });

            // Attempt duplicate acceptance by donor1
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = request.BloodRequestId,
                    DonorBloodGroup = "A+"
                }));

            Assert.Contains("already accepted", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Expired_Request_Cannot_Be_Accepted()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(-1)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = request.BloodRequestId,
                    DonorBloodGroup = "A+"
                }));

            Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Cancelled_Request_Cannot_Be_Accepted()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Cancelled,
                CancelledAt = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = request.BloodRequestId,
                    DonorBloodGroup = "A+"
                }));

            Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Completed_Request_Cannot_Be_Accepted()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                FulfilledUnits = 1,
                Status = BloodRequestStatus.Completed,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = request.BloodRequestId,
                    DonorBloodGroup = "A+"
                }));

            Assert.Contains("completed", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Matched_Request_Cannot_Be_Accepted()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);

            // Simulate Student 2 DonorPatientMatch
            var match = new DonorPatientMatch
            {
                MatchId = Guid.NewGuid(),
                BloodRequestId = request.BloodRequestId,
                DonorUserId = Guid.NewGuid(),
                DoctorId = Guid.NewGuid(),
                Status = MatchStatus.Matched
            };
            await context.DonorPatientMatches.AddAsync(match);
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = request.BloodRequestId,
                    DonorBloodGroup = "A+"
                }));

            Assert.Contains("fulfilled", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Verified_Donor_Validation_Enforced()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var unverifiedDonor = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = "Unverified",
                LastName = "Donor",
                Email = "unverified@lifelink.org"
            };
            await context.Users.AddAsync(unverifiedDonor);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.AcceptRequestAsync(unverifiedDonor.UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = request.BloodRequestId,
                    DonorBloodGroup = "A+"
                }));

            Assert.Contains("verified", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Multiple_Donors_Can_Accept_Same_Request()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, donor2) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            // Request requires 1 unit, but both donor1 and donor2 can accept (oversubscription)
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var acc1 = await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "A+"
            });

            var acc2 = await acceptanceService.AcceptRequestAsync(donor2.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "O-"
            });

            Assert.NotNull(acc1);
            Assert.NotNull(acc2);
            Assert.Equal("Accepted", acc1.Status);
            Assert.Equal("Accepted", acc2.Status);

            var acceptances = await acceptanceService.GetRequestAcceptancesAsync(request.BloodRequestId);
            Assert.Equal(2, acceptances.Count);
        }

        [Fact]
        public async Task Acceptance_Cancellation_Works()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var acc = await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "A+"
            });

            var cancelled = await acceptanceService.CancelAcceptanceAsync(acc.AcceptanceId, donor1.UserId);
            Assert.Equal("Cancelled", cancelled.Status);
            Assert.NotNull(cancelled.CancelledAt);
        }

        // =========================================================================
        // GROUP F: Doctor Review, Selection & Partial Fulfillment
        // =========================================================================

        [Fact]
        public async Task Get_Request_Acceptances_Works()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "A+"
            });

            var list = await acceptanceService.GetRequestAcceptancesAsync(request.BloodRequestId);
            Assert.Single(list);
            Assert.Equal("Bob Jones", list[0].DonorName);
            Assert.Equal("bob@lifelink.org", list[0].DonorEmail);
        }

        [Fact]
        public async Task Doctor_Can_Finalize_Donor_Selection_Full_Fulfillment()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, donor2) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var acc1 = await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "A+"
            });

            var acc2 = await acceptanceService.AcceptRequestAsync(donor2.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "O-"
            });

            var doctor = await context.Doctors.FirstAsync();

            // Doctor selects donor 1 (1 unit required -> fully fulfills)
            var response = await acceptanceService.FinalizeDonorSelectionAsync(
                request.BloodRequestId,
                new List<Guid> { acc1.AcceptanceId },
                doctor.DoctorId);

            Assert.Equal(1, response.MatchedDonors);
            Assert.Equal(1, response.RejectedDonors);
            Assert.Equal(1, response.TotalFulfilledUnits);
            Assert.Equal(0, response.RemainingUnits);

            var updatedRequest = await context.BloodRequests.FindAsync(request.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Completed, updatedRequest!.Status);
            Assert.Equal(1, updatedRequest.FulfilledUnits);

            var updatedAcc1 = await context.Acceptances.FindAsync(acc1.AcceptanceId);
            Assert.Equal(AcceptanceStatus.Matched, updatedAcc1!.Status);

            var updatedAcc2 = await context.Acceptances.FindAsync(acc2.AcceptanceId);
            Assert.Equal(AcceptanceStatus.Rejected, updatedAcc2!.Status);
            Assert.Contains("fulfilled by other", updatedAcc2.RejectionReason);
        }

        [Fact]
        public async Task Doctor_Can_Finalize_Donor_Selection_Partial_Fulfillment()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            // Requires 5 units, doctor selects 1 donor -> remains Approved
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 5,
                FulfilledUnits = 0,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var acc1 = await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "A+"
            });

            var doctor = await context.Doctors.FirstAsync();

            var response = await acceptanceService.FinalizeDonorSelectionAsync(
                request.BloodRequestId,
                new List<Guid> { acc1.AcceptanceId },
                doctor.DoctorId);

            Assert.Equal(1, response.MatchedDonors);
            Assert.Equal(0, response.RejectedDonors);
            Assert.Equal(1, response.TotalFulfilledUnits);
            Assert.Equal(4, response.RemainingUnits);

            var updatedRequest = await context.BloodRequests.FindAsync(request.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Approved, updatedRequest!.Status);
            Assert.Equal(1, updatedRequest.FulfilledUnits);
        }

        [Fact]
        public async Task FulfilledUnits_Increments_Correctly_After_Doctor_Selection()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, donor2) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 3,
                FulfilledUnits = 0,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var acc1 = await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "A+"
            });

            var acc2 = await acceptanceService.AcceptRequestAsync(donor2.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "O-"
            });

            var doctor = await context.Doctors.FirstAsync();

            var response = await acceptanceService.FinalizeDonorSelectionAsync(
                request.BloodRequestId,
                new List<Guid> { acc1.AcceptanceId, acc2.AcceptanceId },
                doctor.DoctorId);

            Assert.Equal(2, response.TotalFulfilledUnits);
            Assert.Equal(1, response.RemainingUnits);
        }

        [Fact]
        public async Task Fulfilled_Request_Rejects_New_Acceptances()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 2,
                FulfilledUnits = 2,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = request.BloodRequestId,
                    DonorBloodGroup = "A+"
                }));

            Assert.Contains("fulfilled", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Selection_Count_Cannot_Exceed_Remaining_Units()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, donor2) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                FulfilledUnits = 0,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var acc1 = await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "A+"
            });

            var acc2 = await acceptanceService.AcceptRequestAsync(donor2.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "O-"
            });

            var doctor = await context.Doctors.FirstAsync();

            // Trying to select 2 donors when only 1 unit required
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.FinalizeDonorSelectionAsync(
                    request.BloodRequestId,
                    new List<Guid> { acc1.AcceptanceId, acc2.AcceptanceId },
                    doctor.DoctorId));

            Assert.Contains("cannot exceed remaining", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Empty_Donor_Selection_Is_Rejected()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var doctor = await context.Doctors.FirstAsync();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                acceptanceService.FinalizeDonorSelectionAsync(
                    request.BloodRequestId,
                    new List<Guid>(),
                    doctor.DoctorId));
        }

        [Fact]
        public async Task Request_Already_Completed_Cannot_Be_Finalized()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                FulfilledUnits = 1,
                Status = BloodRequestStatus.Completed,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var doctor = await context.Doctors.FirstAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.FinalizeDonorSelectionAsync(
                    request.BloodRequestId,
                    new List<Guid> { Guid.NewGuid() },
                    doctor.DoctorId));

            Assert.Contains("completed", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Already_Matched_Donor_Cannot_Be_Selected_Again()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 2,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);

            var acc = new Acceptance
            {
                AcceptanceId = Guid.NewGuid(),
                BloodRequestId = request.BloodRequestId,
                DonorUserId = donor1.UserId,
                Status = AcceptanceStatus.Accepted
            };
            await context.Acceptances.AddAsync(acc);

            // Existing match for donor1 in Student 2
            var match = new DonorPatientMatch
            {
                MatchId = Guid.NewGuid(),
                BloodRequestId = request.BloodRequestId,
                DonorUserId = donor1.UserId,
                DoctorId = Guid.NewGuid(),
                Status = MatchStatus.Matched
            };
            await context.DonorPatientMatches.AddAsync(match);
            await context.SaveChangesAsync();

            var doctor = await context.Doctors.FirstAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.FinalizeDonorSelectionAsync(
                    request.BloodRequestId,
                    new List<Guid> { acc.AcceptanceId },
                    doctor.DoctorId));

            Assert.Contains("already been matched", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // =========================================================================
        // GROUP G: Scope Enhancement Tests (Issues 1 to 7)
        // =========================================================================

        [Fact]
        public async Task Doctor_Authorization_Validation_Enforced()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var acc = await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "A+"
            });

            // Unknown or non-assigned doctor
            var randomDoctorId = Guid.NewGuid();
            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                acceptanceService.FinalizeDonorSelectionAsync(
                    request.BloodRequestId,
                    new List<Guid> { acc.AcceptanceId },
                    randomDoctorId));

            Assert.Contains("Only verified doctors assigned to this hospital", ex.Message);
        }

        [Fact]
        public async Task Donor_Over_Commitment_Prevented()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request1 = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };

            var request2 = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "O+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddRangeAsync(request1, request2);
            await context.SaveChangesAsync();

            // Donor accepts request 1
            var acc1 = await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request1.BloodRequestId,
                DonorBloodGroup = "A+"
            });

            // Transition donor1 into ScreeningPending
            await acceptanceService.UpdateScreeningStatusAsync(acc1.AcceptanceId, AcceptanceStatus.ScreeningPending);

            // Attempt to accept request 2 while in active screening
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = request2.BloodRequestId,
                    DonorBloodGroup = "O+"
                }));

            Assert.Equal("You already have an active donation process.", ex.Message);
        }

        [Fact]
        public async Task Automatic_Request_Expiry_Service_Works()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, _, _) = await SeedBaseDataAsync(context);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RequestExpiryService>.Instance;
            var expiryService = new RequestExpiryService(context, logger);

            var expiredReq = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 2,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(-1),
                Reason = "Old request",
                Priority = "Normal"
            };
            await context.BloodRequests.AddAsync(expiredReq);
            await context.SaveChangesAsync();

            var processedCount = await expiryService.ProcessExpiredRequestsAsync();
            Assert.Equal(1, processedCount);

            var updated = await context.BloodRequests.FindAsync(expiredReq.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Rejected, updated!.Status);
        }

        [Fact]
        public async Task Acceptance_Status_Lifecycle_Transitions_Work()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var acc = await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "A+"
            });
            Assert.Equal("Accepted", acc.Status);

            var step1 = await acceptanceService.UpdateScreeningStatusAsync(acc.AcceptanceId, AcceptanceStatus.ScreeningPending);
            Assert.Equal("ScreeningPending", step1.Status);

            var step2 = await acceptanceService.UpdateScreeningStatusAsync(acc.AcceptanceId, AcceptanceStatus.ScreeningCompleted);
            Assert.Equal("ScreeningCompleted", step2.Status);

            var step3 = await acceptanceService.UpdateScreeningStatusAsync(acc.AcceptanceId, AcceptanceStatus.Verified);
            Assert.Equal("Verified", step3.Status);

            var doctor = await context.Doctors.FirstAsync();
            var final = await acceptanceService.FinalizeDonorSelectionAsync(
                request.BloodRequestId,
                new List<Guid> { acc.AcceptanceId },
                doctor.DoctorId);

            Assert.Equal(1, final.MatchedDonors);
        }

        [Fact]
        public async Task Request_Fulfillment_History_Recorded_On_Finalization()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, _) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);
            var requestService = new BloodRequestService(context);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var acc = await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "A+"
            });

            var doctor = await context.Doctors.FirstAsync();
            await acceptanceService.FinalizeDonorSelectionAsync(
                request.BloodRequestId,
                new List<Guid> { acc.AcceptanceId },
                doctor.DoctorId);

            var history = (await requestService.GetRequestFulfillmentHistoryAsync(request.BloodRequestId)).ToList();
            Assert.Single(history);
            Assert.Equal(donor1.UserId, history[0].DonorUserId);
            Assert.Equal(acc.AcceptanceId, history[0].AcceptanceId);
        }

        [Fact]
        public async Task Request_Analytics_Endpoint_Returns_Accurate_Metrics()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donor1, donor2) = await SeedBaseDataAsync(context);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);
            var requestService = new BloodRequestService(context);

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 2,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var acc1 = await acceptanceService.AcceptRequestAsync(donor1.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "A+"
            });

            var acc2 = await acceptanceService.AcceptRequestAsync(donor2.UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "O-"
            });

            var doctor = await context.Doctors.FirstAsync();
            await acceptanceService.FinalizeDonorSelectionAsync(
                request.BloodRequestId,
                new List<Guid> { acc1.AcceptanceId },
                doctor.DoctorId);

            var analytics = await requestService.GetRequestAnalyticsAsync(request.BloodRequestId);
            Assert.Equal(2, analytics.UnitsRequired);
            Assert.Equal(1, analytics.FulfilledUnits);
            Assert.Equal(1, analytics.RemainingUnits);
            Assert.Equal(2, analytics.AcceptanceCount);
            Assert.Equal(1, analytics.MatchedCount);
            Assert.Equal(50, analytics.CompletionPercentage);
        }
    }
}
