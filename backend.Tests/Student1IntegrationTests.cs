using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    public class Student1IntegrationTests
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

        private async Task<(Hospital hospital, Doctor doctor, User patient, List<User> donors)> SetupScenarioAsync(AppDbContext context, int donorCount = 10)
        {
            var hospital = new Hospital
            {
                HospitalId = Guid.NewGuid(),
                Name = "Apex Hospital",
                IsVerified = true
            };

            var doctor = new Doctor
            {
                DoctorId = Guid.NewGuid(),
                HospitalId = hospital.HospitalId,
                FirstName = "Lisa",
                LastName = "Cuddy",
                Email = "cuddy@apex.org",
                IsActive = true
            };

            var patient = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = "John",
                LastName = "Doe",
                Email = "john.doe@lifelink.org"
            };

            await context.Hospitals.AddAsync(hospital);
            await context.Doctors.AddAsync(doctor);
            await context.Users.AddAsync(patient);

            var donors = new List<User>();
            for (int i = 1; i <= donorCount; i++)
            {
                var donor = new User
                {
                    UserId = Guid.NewGuid(),
                    FirstName = $"Donor{i}",
                    LastName = "Test",
                    Email = $"donor{i}@lifelink.org"
                };
                donors.Add(donor);
                await context.Users.AddAsync(donor);

                // Verified donor in Student 2 table
                var verification = new DonorVerification
                {
                    DonorVerificationId = Guid.NewGuid(),
                    AcceptanceId = donor.UserId,
                    DoctorId = doctor.DoctorId,
                    Status = VerificationStatus.Approved
                };
                await context.DonorVerifications.AddAsync(verification);
            }

            await context.SaveChangesAsync();

            return (hospital, doctor, patient, donors);
        }

        /// <summary>
        /// SCENARIO 1:
        /// Patient creates request -> Doctor approves request -> 10 donors accept -> Doctor selects 2 -> Request partially fulfilled
        /// </summary>
        [Fact]
        public async Task Integration_Scenario_1_Partial_Fulfillment_Workflow()
        {
            var context = GetInMemoryDbContext();
            var (hospital, doctor, patient, donors) = await SetupScenarioAsync(context, donorCount: 10);
            var compatibility = new BloodCompatibilityService();
            var requestService = new BloodRequestService(context);
            var acceptanceService = new AcceptanceService(context, compatibility);

            // 1. Patient creates request requiring 5 units
            var createdReq = await requestService.CreateRequestAsync(patient.UserId, new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "O+",
                UnitsRequired = 5,
                Reason = "Accident trauma surgery",
                Priority = "Critical"
            });
            Assert.Equal("Pending", createdReq.Status);

            // 2. Doctor/Hospital approves request
            var requestEntity = await context.BloodRequests.FindAsync(createdReq.BloodRequestId);
            requestEntity!.Status = BloodRequestStatus.Approved;
            await context.SaveChangesAsync();

            // 3. 10 compatible donors accept request
            var acceptanceIds = new List<Guid>();
            foreach (var donor in donors)
            {
                var acc = await acceptanceService.AcceptRequestAsync(donor.UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = createdReq.BloodRequestId,
                    DonorBloodGroup = "O+"
                });
                Assert.Equal("Accepted", acc.Status);
                acceptanceIds.Add(acc.AcceptanceId);
            }
            Assert.Equal(10, acceptanceIds.Count);

            // 4. Doctor approves 2 screened donors (reserved slots), then their donations are recorded
            var selectedTwo = acceptanceIds.Take(2).ToList();
            await TestReservations.ReserveAsync(context, selectedTwo.ToArray());
            var selectionResult = await acceptanceService.FinalizeDonorSelectionAsync(
                createdReq.BloodRequestId,
                selectedTwo,
                doctor.DoctorId);

            // 5. Verification: Request partially fulfilled
            Assert.Equal(2, selectionResult.MatchedDonors);
            Assert.Equal(0, selectionResult.RejectedDonors); // Not completed yet, so others remain candidate
            Assert.Equal(2, selectionResult.TotalFulfilledUnits);
            Assert.Equal(3, selectionResult.RemainingUnits);

            var refreshedReq = await context.BloodRequests.FindAsync(createdReq.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Approved, refreshedReq!.Status);
            Assert.Equal(2, refreshedReq.FulfilledUnits);

            // Audit history has 2 records
            var history = await requestService.GetRequestFulfillmentHistoryAsync(createdReq.BloodRequestId);
            Assert.Equal(2, history.Count());
        }

        /// <summary>
        /// SCENARIO 2:
        /// Request becomes fully completed -> Remaining donors automatically rejected
        /// </summary>
        [Fact]
        public async Task Integration_Scenario_2_Full_Completion_And_Automatic_Rejections()
        {
            var context = GetInMemoryDbContext();
            var (hospital, doctor, patient, donors) = await SetupScenarioAsync(context, donorCount: 5);
            var compatibility = new BloodCompatibilityService();
            var requestService = new BloodRequestService(context);
            var acceptanceService = new AcceptanceService(context, compatibility);

            // Request requires 2 units
            var createdReq = await requestService.CreateRequestAsync(patient.UserId, new CreateBloodRequestDto
            {
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 2,
                Reason = "Planned procedure",
                Priority = "Normal"
            });

            var requestEntity = await context.BloodRequests.FindAsync(createdReq.BloodRequestId);
            requestEntity!.Status = BloodRequestStatus.Approved;
            await context.SaveChangesAsync();

            // 5 donors accept
            var acceptanceIds = new List<Guid>();
            foreach (var donor in donors)
            {
                var acc = await acceptanceService.AcceptRequestAsync(donor.UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = createdReq.BloodRequestId,
                    DonorBloodGroup = "A+"
                });
                acceptanceIds.Add(acc.AcceptanceId);
            }

            // Doctor approves and records the required count (2 donors)
            var selectedTwo = acceptanceIds.Take(2).ToList();
            await TestReservations.ReserveAsync(context, selectedTwo.ToArray());
            var selectionResult = await acceptanceService.FinalizeDonorSelectionAsync(
                createdReq.BloodRequestId,
                selectedTwo,
                doctor.DoctorId);

            Assert.Equal(2, selectionResult.MatchedDonors);
            Assert.Equal(3, selectionResult.RejectedDonors);
            Assert.Equal(2, selectionResult.TotalFulfilledUnits);
            Assert.Equal(0, selectionResult.RemainingUnits);

            var refreshedReq = await context.BloodRequests.FindAsync(createdReq.BloodRequestId);
            Assert.Equal(BloodRequestStatus.Completed, refreshedReq!.Status);

            // Verify rejected acceptances have the correct reason
            var allAcceptances = await context.Acceptances
                .Where(a => a.BloodRequestId == createdReq.BloodRequestId)
                .ToListAsync();

            var rejected = allAcceptances.Where(a => a.Status == AcceptanceStatus.Rejected).ToList();
            Assert.Equal(3, rejected.Count);
            Assert.All(rejected, r => Assert.Equal("Required donor count has been fulfilled by other selected donors.", r.RejectionReason));
        }

        /// <summary>
        /// SCENARIO 3:
        /// Expired request cannot be accepted
        /// </summary>
        [Fact]
        public async Task Integration_Scenario_3_Expired_Request_Cannot_Be_Accepted()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donors) = await SetupScenarioAsync(context, donorCount: 1);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            var expiredReq = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "O-",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddMinutes(-5),
                Reason = "Urgent past",
                Priority = "Critical"
            };
            await context.BloodRequests.AddAsync(expiredReq);
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.AcceptRequestAsync(donors[0].UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = expiredReq.BloodRequestId,
                    DonorBloodGroup = "O-"
                }));

            Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// SCENARIO 4:
        /// Concurrent finalization attempt -> Guarantees FulfilledUnits <= UnitsRequired
        /// </summary>
        [Fact]
        public async Task Integration_Scenario_4_Concurrent_Finalization_Guarantees_Integrity()
        {
            var context = GetInMemoryDbContext();
            var (hospital, doctor, patient, donors) = await SetupScenarioAsync(context, donorCount: 4);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            // Request requires 2 units
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "A+",
                UnitsRequired = 2,
                FulfilledUnits = 0,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var accList = new List<Guid>();
            foreach (var donor in donors)
            {
                var acc = await acceptanceService.AcceptRequestAsync(donor.UserId, new CreateAcceptanceDto
                {
                    BloodRequestId = request.BloodRequestId,
                    DonorBloodGroup = "A+"
                });
                accList.Add(acc.AcceptanceId);
            }

            // Doctor 1 records 2 approved donations (full fulfillment)
            await TestReservations.ReserveAsync(context, accList[0], accList[1]);
            await acceptanceService.FinalizeDonorSelectionAsync(
                request.BloodRequestId,
                new List<Guid> { accList[0], accList[1] },
                doctor.DoctorId);

            // Concurrent attempt to finalize more donors on already completed request
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                acceptanceService.FinalizeDonorSelectionAsync(
                    request.BloodRequestId,
                    new List<Guid> { accList[2] },
                    doctor.DoctorId));

            Assert.Contains("completed", ex.Message, StringComparison.OrdinalIgnoreCase);

            var finalReq = await context.BloodRequests.FindAsync(request.BloodRequestId);
            Assert.True(finalReq!.FulfilledUnits <= finalReq.UnitsRequired);
            Assert.Equal(2, finalReq.FulfilledUnits);
        }

        /// <summary>
        /// SCENARIO 5:
        /// Unauthorized doctor cannot finalize selection
        /// </summary>
        [Fact]
        public async Task Integration_Scenario_5_Unauthorized_Doctor_Cannot_Finalize_Selection()
        {
            var context = GetInMemoryDbContext();
            var (hospital, _, patient, donors) = await SetupScenarioAsync(context, donorCount: 2);
            var compatibility = new BloodCompatibilityService();
            var acceptanceService = new AcceptanceService(context, compatibility);

            // Second hospital and doctor
            var otherHospital = new Hospital
            {
                HospitalId = Guid.NewGuid(),
                Name = "Other Hospital",
                IsVerified = true
            };
            var unauthorizedDoctor = new Doctor
            {
                DoctorId = Guid.NewGuid(),
                HospitalId = otherHospital.HospitalId,
                FirstName = "Unassigned",
                LastName = "Doctor",
                IsActive = true
            };
            await context.Hospitals.AddAsync(otherHospital);
            await context.Doctors.AddAsync(unauthorizedDoctor);
            await context.SaveChangesAsync();

            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(),
                PatientUserId = patient.UserId,
                HospitalId = hospital.HospitalId,
                BloodGroup = "B+",
                UnitsRequired = 1,
                Status = BloodRequestStatus.Approved,
                ExpiryDate = DateTime.UtcNow.AddDays(5)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var acc = await acceptanceService.AcceptRequestAsync(donors[0].UserId, new CreateAcceptanceDto
            {
                BloodRequestId = request.BloodRequestId,
                DonorBloodGroup = "B+"
            });

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                acceptanceService.FinalizeDonorSelectionAsync(
                    request.BloodRequestId,
                    new List<Guid> { acc.AcceptanceId },
                    unauthorizedDoctor.DoctorId));

            Assert.Equal("Only verified doctors assigned to this hospital can finalize donor selection.", ex.Message);
        }
    }
}
