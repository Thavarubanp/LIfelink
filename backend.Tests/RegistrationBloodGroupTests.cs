using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.Controllers;
using LifeLink.Data;
using LifeLink.DTOs.Auth;
using LifeLink.DTOs.Profiles;
using LifeLink.Entities;
using LifeLink.Services.Auth;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Phase 1: optional blood group at registration (4.2) and adding it later in the profile (4.3).
    /// </summary>
    public class RegistrationBloodGroupTests
    {
        private sealed class FakeCurrentUser : ICurrentUserService
        {
            public Guid? UserId { get; init; }
            public string? Email { get; init; }
            public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
            public bool IsAuthenticated => UserId != null;
        }

        private static AppDbContext NewContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var context = new AppDbContext(options);
            context.Database.EnsureCreated(); // seeds the roles
            return context;
        }

        private static AuthService NewAuthService(AppDbContext context)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "LifeLink_Super_Secret_Jwt_Signing_Key_2026_For_Development_Only_Must_Be_Long!",
                ["Jwt:Issuer"] = "LifeLinkAPI",
                ["Jwt:Audience"] = "LifeLinkApp",
                ["Jwt:ExpiryMinutes"] = "120"
            }).Build();
            return new AuthService(context, new PasswordHasherService(), new JwtService(configuration),
                new PasswordResetService(context), new Mock<IEmailService>().Object);
        }

        private static RegisterRequestDto Registration(string? bloodGroup) => new()
        {
            FirstName = "Test", LastName = "Donor", Email = "test.donor@example.test", Password = "Password123!",
            PhoneNumber = "0771234567", Gender = "Male", Address = "1 Test Road", BloodGroup = bloodGroup
        };

        private static ProfilesController ControllerFor(AppDbContext context, Guid userId) =>
            new(context, new FakeCurrentUser { UserId = userId, Roles = new[] { "User" } }, null!);

        private static UpdateUserProfileDto ProfileEdit(string? bloodGroup) => new()
        {
            FirstName = "Test", LastName = "Donor", PhoneNumber = "0771234567", Gender = "Male", Address = "1 Test Road", BloodGroup = bloodGroup
        };

        private static async Task<User> AddUserAsync(AppDbContext context, string email, string? bloodGroup = null)
        {
            var user = new User { UserId = Guid.NewGuid(), FirstName = "Test", LastName = "Donor", Email = email, PhoneNumber = "0771234567", BloodGroup = bloodGroup };
            context.Users.Add(user);
            context.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = 1 });
            await context.SaveChangesAsync();
            return user;
        }

        [Theory]
        [InlineData("O+", "O+")]
        [InlineData("ab-", "AB-")]   // normalised
        public async Task Register_With_Blood_Group_Saves_It(string given, string stored)
        {
            var context = NewContext();
            var response = await NewAuthService(context).RegisterAsync(Registration(given));

            var user = await context.Users.SingleAsync(u => u.UserId == response.UserId);
            Assert.Equal(stored, user.BloodGroup);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Register_Without_Blood_Group_Leaves_It_Empty(string? given)
        {
            var context = NewContext();
            var response = await NewAuthService(context).RegisterAsync(Registration(given));

            var user = await context.Users.SingleAsync(u => u.UserId == response.UserId);
            Assert.Null(user.BloodGroup);
        }

        [Theory]
        [InlineData("X+")]
        [InlineData("Don't know")]
        public async Task Register_With_Invalid_Blood_Group_Is_Refused_And_Nothing_Is_Saved(string given)
        {
            var context = NewContext();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NewAuthService(context).RegisterAsync(Registration(given)));
            Assert.Equal("Invalid blood group.", ex.Message);
            Assert.False(await context.Users.AnyAsync());
        }

        [Fact]
        public async Task Owner_Adds_Blood_Group_When_Empty_And_Profile_Shows_It()
        {
            var context = NewContext();
            var user = await AddUserAsync(context, "test.donor@example.test");
            var controller = ControllerFor(context, user.UserId);

            var result = await controller.UpdateUserProfile(user.UserId, ProfileEdit("b+"));

            var profile = Assert.IsType<UserProfileDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal("B+", profile.BloodGroup);
            Assert.Equal("B+", (await context.Users.SingleAsync(u => u.UserId == user.UserId)).BloodGroup);

            var shown = Assert.IsType<UserProfileDto>(Assert.IsType<OkObjectResult>(await controller.GetUserProfile(user.UserId)).Value);
            Assert.Equal("B+", shown.BloodGroup);
        }

        [Fact]
        public async Task Another_User_Cannot_Add_The_Blood_Group()
        {
            var context = NewContext();
            var owner = await AddUserAsync(context, "test.owner@example.test");
            var other = await AddUserAsync(context, "test.other@example.test");

            var result = await ControllerFor(context, other.UserId).UpdateUserProfile(owner.UserId, ProfileEdit("A+"));

            Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
            Assert.Null((await context.Users.SingleAsync(u => u.UserId == owner.UserId)).BloodGroup);
        }

        [Fact]
        public async Task Profile_Refuses_An_Invalid_Blood_Group()
        {
            var context = NewContext();
            var user = await AddUserAsync(context, "test.donor@example.test");

            var result = await ControllerFor(context, user.UserId).UpdateUserProfile(user.UserId, ProfileEdit("Z-"));

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Null((await context.Users.SingleAsync(u => u.UserId == user.UserId)).BloodGroup);
        }
    }
}
