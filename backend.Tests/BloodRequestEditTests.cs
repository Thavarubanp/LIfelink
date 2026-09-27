using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LifeLink.Controllers;
using LifeLink.Data;
using LifeLink.DTOs.BloodRequests;
using LifeLink.Entities;
using LifeLink.Services.BloodRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>A patient edits the blood group and units of their own request while it is still Pending.</summary>
    public class BloodRequestEditTests
    {
        private sealed class Seed
        {
            public AppDbContext Context = null!;
            public Hospital Hospital = null!;
            public User Patient = null!;
            public User Other = null!;
            public BloodRequestService Service = null!;
        }

        private static async Task<Seed> SeedAsync()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();
            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Venus Hospital", Email = "venus@h.org", IsVerified = true };
            var patient = new User { UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Ient", Email = "pat@h.org" };
            var other = new User { UserId = Guid.NewGuid(), FirstName = "Oth", LastName = "Er", Email = "other@h.org" };
            await context.Hospitals.AddAsync(hospital);
            await context.Users.AddRangeAsync(patient, other);
            await context.SaveChangesAsync();
            return new Seed { Context = context, Hospital = hospital, Patient = patient, Other = other, Service = new BloodRequestService(context) };
        }

        private static Task<BloodRequestResponseDto> CreateAsync(Seed s, string group = "O+", int units = 2) =>
            s.Service.CreateRequestAsync(s.Patient.UserId, new CreateBloodRequestDto
            {
                HospitalId = s.Hospital.HospitalId, BloodGroup = group, UnitsRequired = units, Reason = "Surgery", Priority = "High"
            });

        [Fact]
        public async Task The_Owner_Can_Change_Only_Blood_Group_And_Units_While_Pending()
        {
            var s = await SeedAsync();
            var created = await CreateAsync(s);
            var before = await s.Context.BloodRequests.AsNoTracking().SingleAsync();

            var updated = await s.Service.UpdatePendingRequestAsync(created.BloodRequestId, s.Patient.UserId,
                new UpdateBloodRequestDto { BloodGroup = "ab-", UnitsRequired = 4 });

            Assert.Equal("AB-", updated.BloodGroup); // normalised like creation
            Assert.Equal(4, updated.UnitsRequired);
            var after = await s.Context.BloodRequests.AsNoTracking().SingleAsync();
            Assert.Equal(before.HospitalId, after.HospitalId);
            Assert.Equal(before.Priority, after.Priority);
            Assert.Equal(before.Reason, after.Reason);
            Assert.Equal(before.ExpiryDate, after.ExpiryDate);
            Assert.Equal(before.PatientUserId, after.PatientUserId);
            Assert.Equal(BloodRequestStatus.Pending, after.Status);
        }

        [Fact]
        public async Task Only_The_Creator_Can_Edit()
        {
            var s = await SeedAsync();
            var created = await CreateAsync(s);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Service.UpdatePendingRequestAsync(created.BloodRequestId, s.Other.UserId,
                new UpdateBloodRequestDto { BloodGroup = "A+", UnitsRequired = 1 }));
            Assert.Equal("O+", (await s.Context.BloodRequests.AsNoTracking().SingleAsync()).BloodGroup);
        }

        [Fact]
        public void The_Endpoint_Is_For_Donor_Patient_Accounts_Only()
        {
            var attribute = typeof(BloodRequestsController).GetMethod(nameof(BloodRequestsController.UpdateRequest))!.GetCustomAttribute<AuthorizeAttribute>();
            Assert.Equal("User", attribute!.Roles); // hospital staff, doctors and admins are refused
        }

        [Theory]
        [InlineData(BloodRequestStatus.Verified)]
        [InlineData(BloodRequestStatus.Approved)]
        [InlineData(BloodRequestStatus.Rejected)]
        [InlineData(BloodRequestStatus.Completed)]
        [InlineData(BloodRequestStatus.Cancelled)]
        [InlineData(BloodRequestStatus.Deleted)]
        public async Task Editing_Is_Blocked_Once_The_Request_Is_No_Longer_Pending(BloodRequestStatus status)
        {
            var s = await SeedAsync();
            var created = await CreateAsync(s);
            var request = await s.Context.BloodRequests.SingleAsync();
            request.Status = status;
            await s.Context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.UpdatePendingRequestAsync(created.BloodRequestId, s.Patient.UserId,
                new UpdateBloodRequestDto { BloodGroup = "A+", UnitsRequired = 1 }));
            Assert.Contains("Only pending requests can be edited", ex.Message);
        }

        [Fact]
        public async Task An_Expired_Pending_Request_Cannot_Be_Edited()
        {
            var s = await SeedAsync();
            var created = await CreateAsync(s);
            (await s.Context.BloodRequests.SingleAsync()).ExpiryDate = DateTime.UtcNow.AddMinutes(-1);
            await s.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.UpdatePendingRequestAsync(created.BloodRequestId, s.Patient.UserId,
                new UpdateBloodRequestDto { BloodGroup = "A+", UnitsRequired = 1 }));
        }

        [Theory]
        [InlineData("X+", 2)]
        [InlineData("", 2)]
        [InlineData("A+", 0)]
        [InlineData("A+", 11)]
        public async Task The_Same_Validation_As_Creation_Applies(string group, int units)
        {
            var s = await SeedAsync();
            var created = await CreateAsync(s);

            await Assert.ThrowsAsync<ArgumentException>(() => s.Service.UpdatePendingRequestAsync(created.BloodRequestId, s.Patient.UserId,
                new UpdateBloodRequestDto { BloodGroup = group, UnitsRequired = units }));
            var saved = await s.Context.BloodRequests.AsNoTracking().SingleAsync();
            Assert.Equal("O+", saved.BloodGroup);
            Assert.Equal(2, saved.UnitsRequired);
        }

        [Fact]
        public async Task It_Cannot_Duplicate_Another_Active_Request_For_The_Same_Hospital_And_Group()
        {
            var s = await SeedAsync();
            await CreateAsync(s, "A+");
            var second = await CreateAsync(s, "O+");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.UpdatePendingRequestAsync(second.BloodRequestId, s.Patient.UserId,
                new UpdateBloodRequestDto { BloodGroup = "A+", UnitsRequired = 2 }));
            Assert.Contains("already exists", ex.Message);

            // Keeping its own group is not a duplicate of itself
            var sameGroup = await s.Service.UpdatePendingRequestAsync(second.BloodRequestId, s.Patient.UserId,
                new UpdateBloodRequestDto { BloodGroup = "O+", UnitsRequired = 5 });
            Assert.Equal(5, sameGroup.UnitsRequired);
        }

        [Fact]
        public async Task A_Missing_Request_Is_Not_Found()
        {
            var s = await SeedAsync();
            await Assert.ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(() => s.Service.UpdatePendingRequestAsync(Guid.NewGuid(), s.Patient.UserId,
                new UpdateBloodRequestDto { BloodGroup = "A+", UnitsRequired = 1 }));
        }
    }
}
