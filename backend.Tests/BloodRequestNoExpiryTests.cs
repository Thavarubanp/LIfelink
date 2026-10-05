using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using LifeLink.Services.BloodCompatibility;
using LifeLink.Services.BloodRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeLink.Tests
{
    public class BloodRequestNoExpiryTests
    {
        [Theory]
        [InlineData("Normal")]
        [InlineData("High")]
        [InlineData("Critical")]
        public async Task Old_Approved_Requests_Remain_Public_And_Acceptable_For_Every_Priority(string priority)
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();
            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "General", Email = "general@h.org", IsVerified = true };
            var patient = new User { UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Ient", Email = "patient@x.org", AccountStatus = AccountStatus.Active };
            var donor = new User
            {
                UserId = Guid.NewGuid(), FirstName = "Don", LastName = "Or", Email = "donor@x.org", BloodGroup = "A+",
                DateOfBirth = DateTime.UtcNow.AddYears(-30), AccountStatus = AccountStatus.Active
            };
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(), PatientUserId = patient.UserId, HospitalId = hospital.HospitalId,
                BloodGroup = "A+", UnitsRequired = 2, Reason = "Treatment", Priority = priority,
                Status = BloodRequestStatus.Approved, CreatedAt = DateTime.UtcNow.AddDays(-40),
                ExpiryDate = DateTime.UtcNow.AddDays(-33)
            };
            await context.AddRangeAsync(hospital, patient, donor, request);
            await context.SaveChangesAsync();

            Assert.Contains(await new BloodRequestService(context).GetPublicRequestsAsync(), r => r.BloodRequestId == request.BloodRequestId);
            var acceptance = await new AcceptanceService(context, new BloodCompatibilityService())
                .AcceptRequestAsync(donor.UserId, new CreateAcceptanceDto { BloodRequestId = request.BloodRequestId, DonorBloodGroup = donor.BloodGroup });

            Assert.Equal("Accepted", acceptance.Status);
            Assert.Equal(BloodRequestStatus.Approved, request.Status);
        }

        [Fact]
        public async Task Elapsed_Time_And_The_Legacy_Sweep_Do_Not_Change_Status()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(), PatientUserId = Guid.NewGuid(), HospitalId = Guid.NewGuid(), BloodGroup = "O+",
                UnitsRequired = 1, Reason = "Treatment", Priority = "High", Status = BloodRequestStatus.Approved,
                CreatedAt = DateTime.UtcNow.AddDays(-20), ExpiryDate = DateTime.UtcNow.AddDays(-13)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            Assert.Equal(0, await new RequestExpiryService(context, NullLogger<RequestExpiryService>.Instance).ProcessExpiredRequestsAsync());
            Assert.Equal(BloodRequestStatus.Approved, request.Status);
            Assert.Null(request.RejectionReason);
        }

        [Theory]
        [InlineData(BloodRequestStatus.Completed)]
        [InlineData(BloodRequestStatus.Cancelled)]
        [InlineData(BloodRequestStatus.Rejected)]
        [InlineData(BloodRequestStatus.Deleted)]
        public async Task Explicitly_Closed_Statuses_Remain_Out_Of_The_Public_Feed(BloodRequestStatus status)
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();
            var request = new BloodRequest
            {
                BloodRequestId = Guid.NewGuid(), PatientUserId = Guid.NewGuid(), HospitalId = Guid.NewGuid(), BloodGroup = "O+",
                UnitsRequired = 1, Reason = "Treatment", Priority = "Normal", Status = status,
                ExpiryDate = DateTime.UtcNow.AddYears(1)
            };
            await context.BloodRequests.AddAsync(request);
            await context.SaveChangesAsync();

            Assert.Empty(await new BloodRequestService(context).GetPublicRequestsAsync());
        }
    }
}
