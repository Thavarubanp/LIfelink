using System;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Hospitals;
using LifeLink.DTOs.Inventory;
using LifeLink.Entities;
using LifeLink.Services.Hospitals;
using LifeLink.Services.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Hospital registration-number uniqueness on registration and on registration replies, and the empty-ID
    /// placeholder guard. (The database unique index itself is not enforced by the in-memory provider.)
    /// </summary>
    public class HospitalRegistrationNumberTests
    {
        private const string Duplicate = "A hospital with this registration number already exists.";

        private static AppDbContext NewContext()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            context.Database.EnsureCreated();
            return context;
        }

        private static CreateHospitalDto Hospital(string email, string? registration, string license = "LIC-1") => new()
        {
            Name = "Test Hospital", Email = email, LicenseNumber = license, Address = "Addr", ContactNumber = "0110000000",
            RegistrationNumber = registration, ContactPersonName = "Dr. Test", ContactPersonPhone = "0771234567"
        };

        private static HospitalRegistrationReplyDto Reply(string registration) => new()
        {
            Message = "Corrected the registration number.", RegistrationNumber = registration
        };

        // Replies are only possible on a rejected registration
        private static async Task RejectAsync(AppDbContext context, Guid hospitalId)
        {
            var hospital = await context.Hospitals.FirstAsync(h => h.HospitalId == hospitalId);
            hospital.ApprovalStatus = ApprovalStatus.Rejected;
            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task Registration_Stores_Number_Trimmed_And_Uppercase()
        {
            var service = new HospitalService(NewContext());
            var created = await service.CreateHospitalAsync(Hospital("a@h.org", "  phsrc/ph/1234 "));
            Assert.Equal("PHSRC/PH/1234", created.RegistrationNumber);
        }

        [Theory]
        [InlineData("PHSRC/PH/1234")]
        [InlineData("phsrc/ph/1234")]
        [InlineData("  PHSRC/PH/1234  ")]
        public async Task Registration_With_Existing_Number_Is_Rejected(string registration)
        {
            var context = NewContext();
            var service = new HospitalService(context);
            await service.CreateHospitalAsync(Hospital("a@h.org", "PHSRC/PH/1234"));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateHospitalAsync(Hospital("b@h.org", registration)));
            Assert.Equal(Duplicate, ex.Message);
            Assert.Equal(1, await context.Hospitals.CountAsync());
        }

        [Fact]
        public async Task Registration_Falling_Back_To_License_Number_Is_Also_Checked()
        {
            var service = new HospitalService(NewContext());
            await service.CreateHospitalAsync(Hospital("a@h.org", "PHSRC/PH/1234"));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateHospitalAsync(Hospital("b@h.org", null, license: "phsrc/ph/1234")));
            Assert.Equal(Duplicate, ex.Message);
        }

        [Fact]
        public async Task Registration_Reply_Cannot_Take_Another_Hospitals_Number_But_Can_Keep_Or_Change_Its_Own()
        {
            var context = NewContext();
            var service = new HospitalService(context);
            await service.CreateHospitalAsync(Hospital("a@h.org", "PHSRC/PH/1234"));
            var second = await service.CreateHospitalAsync(Hospital("b@h.org", "PHSRC/PH/5678"));
            await RejectAsync(context, second.HospitalId);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReplyToRegistrationAsync(second.HospitalId, Reply(" phsrc/ph/1234")));
            Assert.Equal(Duplicate, ex.Message);
            Assert.Equal("PHSRC/PH/5678", (await context.Hospitals.AsNoTracking().FirstAsync(h => h.HospitalId == second.HospitalId)).RegistrationNumber);

            var kept = await service.ReplyToRegistrationAsync(second.HospitalId, Reply("phsrc/ph/5678"));
            Assert.Equal("PHSRC/PH/5678", kept.RegistrationNumber);

            var changed = await service.ReplyToRegistrationAsync(second.HospitalId, Reply(" phsrc/ph/9999 "));
            Assert.Equal("PHSRC/PH/9999", changed.RegistrationNumber);
            Assert.Equal("Rejected", changed.ApprovalStatus); // replies never change the status
        }

        [Fact]
        public async Task Empty_Hospital_Id_Does_Not_Create_A_Placeholder_Hospital()
        {
            var context = NewContext();
            var service = new BloodInventoryService(context);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInventoryAsync(new CreateInventoryDto
            {
                HospitalId = Guid.Empty, BloodGroup = "A+", MinimumThreshold = 1, MaximumCapacity = 10
            }));
            Assert.Equal(0, await context.Hospitals.CountAsync());
        }
    }
}
