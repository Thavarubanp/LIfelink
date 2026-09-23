using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.Controllers;
using LifeLink.Data;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Doctors;
using LifeLink.DTOs.Profiles;
using LifeLink.Entities;
using LifeLink.Services.Auth;
using LifeLink.Services.Common;
using LifeLink.Services.Doctors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// SLMC number uniqueness on doctor creation and doctor own-profile editing.
    /// (The database unique index itself is not enforced by the in-memory provider.)
    /// </summary>
    public class ProfileSlmcTests
    {
        private sealed class FakeCurrentUser : ICurrentUserService
        {
            public Guid? UserId { get; init; }
            public string? Email { get; init; }
            public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
            public bool IsAuthenticated => UserId != null;
        }

        private static async Task<(AppDbContext Context, Hospital Hospital, Doctor Existing)> SeedAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var context = new AppDbContext(options);
            context.Database.EnsureCreated(); // seeds roles, including Doctor

            var hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = "Venus Hospital", Email = "venus@lifelink.org", IsVerified = true };
            var login = new User { UserId = Guid.NewGuid(), FirstName = "Samara", LastName = "Silva", Email = "samara@venus.org", PhoneNumber = "0711111111" };
            var existing = new Doctor
            {
                DoctorId = Guid.NewGuid(), UserId = login.UserId, HospitalId = hospital.HospitalId,
                FirstName = "Samara", LastName = "Silva", Email = "samara@venus.org", LicenseNumber = "SLMC/2006/1234", IsActive = true
            };
            await context.Hospitals.AddAsync(hospital);
            await context.Users.AddAsync(login);
            await context.Doctors.AddAsync(existing);
            await context.SaveChangesAsync();
            return (context, hospital, existing);
        }

        private static CreateDoctorDto NewDoctor(Guid hospitalId, string slmc, string email = "new.doc@venus.org") => new()
        {
            HospitalId = hospitalId, FirstName = "New", LastName = "Doc", Email = email,
            Password = "Passw0rd!", PhoneNumber = "0712345678", LicenseNumber = slmc
        };

        private static ProfilesController ControllerFor(AppDbContext context, Guid userId, string role) =>
            new(context, new FakeCurrentUser { UserId = userId, Roles = new[] { role } }, null!);

        private static UpdateDoctorProfileDto EditDto(string slmc) => new()
        {
            FirstName = "Samara", LastName = "Perera", PhoneNumber = "0779999999", Specialization = "Haematology", LicenseNumber = slmc
        };

        [Theory]
        [InlineData("SLMC/2006/1234")]
        [InlineData("slmc/2006/1234")]      // case-insensitive
        [InlineData("  SLMC/2006/1234  ")]  // ignores surrounding spaces
        public async Task Creating_Doctor_With_Existing_Slmc_Is_Rejected(string slmc)
        {
            var (context, hospital, _) = await SeedAsync();
            var service = new DoctorService(context, new PasswordHasherService());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateDoctorAsync(NewDoctor(hospital.HospitalId, slmc)));
            Assert.Equal("A doctor with this SLMC number already exists.", ex.Message);
            Assert.Equal(1, await context.Doctors.CountAsync());
        }

        [Fact]
        public async Task Creating_Doctor_Stores_Slmc_Trimmed_And_Uppercase_And_Requires_It()
        {
            var (context, hospital, _) = await SeedAsync();
            var service = new DoctorService(context, new PasswordHasherService());

            var created = await service.CreateDoctorAsync(NewDoctor(hospital.HospitalId, "  slmc/2024/555 "));
            Assert.Equal("SLMC/2024/555", created.LicenseNumber);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateDoctorAsync(NewDoctor(hospital.HospitalId, "   ", "blank.slmc@venus.org")));
        }

        [Fact]
        public async Task Doctor_Can_Edit_Own_Profile_Keeping_Own_Slmc_And_Login_Stays_In_Sync()
        {
            var (context, _, existing) = await SeedAsync();
            var controller = ControllerFor(context, existing.UserId!.Value, "Doctor");

            var result = await controller.UpdateDoctorProfile(existing.DoctorId, EditDto(" slmc/2006/1234 "));

            var dto = Assert.IsType<DoctorProfileDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal("SLMC/2006/1234", dto.LicenseNumber);
            Assert.Equal("Perera", dto.LastName);
            Assert.True(dto.CanEdit);

            var login = await context.Users.FindAsync(existing.UserId);
            Assert.Equal("Perera", login!.LastName);
            Assert.Equal("0779999999", login.PhoneNumber);
        }

        [Fact]
        public async Task Doctor_Cannot_Take_Another_Doctors_Slmc()
        {
            var (context, hospital, existing) = await SeedAsync();
            var second = new Doctor
            {
                DoctorId = Guid.NewGuid(), UserId = Guid.NewGuid(), HospitalId = hospital.HospitalId,
                FirstName = "Second", LastName = "Doc", Email = "second@venus.org", LicenseNumber = "SLMC/2010/0001", IsActive = true
            };
            await context.Doctors.AddAsync(second);
            await context.SaveChangesAsync();

            var result = await ControllerFor(context, second.UserId!.Value, "Doctor")
                .UpdateDoctorProfile(second.DoctorId, EditDto("Slmc/2006/1234"));

            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("A doctor with this SLMC number already exists.", Assert.IsType<ApiResponse<object>>(bad.Value).Message);
            Assert.Equal("SLMC/2010/0001", (await context.Doctors.AsNoTracking().FirstAsync(d => d.DoctorId == second.DoctorId)).LicenseNumber);
        }

        [Fact]
        public async Task Doctor_Cannot_Edit_Another_Doctors_Profile()
        {
            var (context, _, existing) = await SeedAsync();

            var result = await ControllerFor(context, Guid.NewGuid(), "Doctor")
                .UpdateDoctorProfile(existing.DoctorId, EditDto("SLMC/2099/9999"));

            Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
            Assert.Equal("Silva", (await context.Doctors.AsNoTracking().FirstAsync()).LastName);
        }
    }
}
