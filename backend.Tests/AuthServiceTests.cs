using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Auth;
using LifeLink.Entities;
using LifeLink.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    public class AuthServiceTests
    {
        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new AppDbContext(options);

            // Seed default roles
            context.Roles.AddRange(
                new Role { RoleId = 1, Name = "User" },
                new Role { RoleId = 2, Name = "HospitalStaff" },
                new Role { RoleId = 3, Name = "Doctor" },
                new Role { RoleId = 4, Name = "Admin" }
            );
            context.SaveChanges();

            return context;
        }

        private IConfiguration GetMockConfiguration()
        {
            var inMemorySettings = new Dictionary<string, string?> {
                {"Jwt:Key", "LifeLink_Super_Secret_Jwt_Signing_Key_2026_For_Development_Only_Must_Be_Long!"},
                {"Jwt:Issuer", "LifeLinkAPI"},
                {"Jwt:Audience", "LifeLinkApp"},
                {"Jwt:ExpiryMinutes", "120"}
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();
        }

        [Fact]
        public async Task RegisterAsync_ValidRequest_CreatesUserWithHashedPasswordAndDefaultRole()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            var authService = new AuthService(context, passwordHasher, jwtService, passwordResetService, mockEmailService.Object);

            var request = new RegisterRequestDto
            {
                FirstName = "Jane",
                LastName = "Doe",
                Email = "Jane.Doe@example.com",
                Password = "Password123!",
                PhoneNumber = "+1234567890",
                Gender = "Female",
                Address = "123 Main St"
            };

            // Act
            var response = await authService.RegisterAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.Equal("jane.doe@example.com", response.Email);
            Assert.Equal("Active", response.AccountStatus);

            var userInDb = await context.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserId == response.UserId);

            Assert.NotNull(userInDb);
            Assert.NotEqual("Password123!", userInDb.PasswordHash);
            Assert.True(passwordHasher.VerifyPassword(userInDb, userInDb.PasswordHash, "Password123!"));
            Assert.Contains(userInDb.UserRoles, ur => ur.Role.Name == "User");
        }

        [Fact]
        public async Task RegisterAsync_DuplicateEmail_ThrowsInvalidOperationException()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            var authService = new AuthService(context, passwordHasher, jwtService, passwordResetService, mockEmailService.Object);

            var request = new RegisterRequestDto
            {
                FirstName = "Jane",
                LastName = "Doe",
                Email = "duplicate@example.com",
                Password = "Password123!"
            };

            await authService.RegisterAsync(request);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => authService.RegisterAsync(request));
        }

        [Fact]
        public async Task LoginAsync_ValidCredentials_ReturnsTokenAndUserInfo()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            var authService = new AuthService(context, passwordHasher, jwtService, passwordResetService, mockEmailService.Object);

            var registerRequest = new RegisterRequestDto
            {
                FirstName = "John",
                LastName = "Smith",
                Email = "john.smith@example.com",
                Password = "Password123!"
            };
            await authService.RegisterAsync(registerRequest);

            var loginRequest = new LoginRequestDto
            {
                Email = "JOHN.SMITH@example.com",
                Password = "Password123!"
            };

            // Act
            var loginResponse = await authService.LoginAsync(loginRequest);

            // Assert
            Assert.NotNull(loginResponse);
            Assert.False(string.IsNullOrWhiteSpace(loginResponse.AccessToken));
            Assert.Equal("john.smith@example.com", loginResponse.User.Email);
            Assert.Contains("User", loginResponse.User.Roles);
        }

        [Fact]
        public async Task LoginAsync_WrongPassword_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            var authService = new AuthService(context, passwordHasher, jwtService, passwordResetService, mockEmailService.Object);

            var registerRequest = new RegisterRequestDto
            {
                FirstName = "John",
                LastName = "Smith",
                Email = "john.smith@example.com",
                Password = "Password123!"
            };
            await authService.RegisterAsync(registerRequest);

            var loginRequest = new LoginRequestDto
            {
                Email = "john.smith@example.com",
                Password = "WrongPassword!"
            };

            // Act & Assert
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authService.LoginAsync(loginRequest));
        }

        [Fact]
        public async Task LoginAsync_SuspendedAccount_ThrowsInvalidOperationException()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            var authService = new AuthService(context, passwordHasher, jwtService, passwordResetService, mockEmailService.Object);

            var user = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = "Suspended",
                LastName = "User",
                Email = "suspended@example.com",
                AccountStatus = AccountStatus.Suspended,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            user.PasswordHash = passwordHasher.HashPassword(user, "Password123!");
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var loginRequest = new LoginRequestDto
            {
                Email = "suspended@example.com",
                Password = "Password123!"
            };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.LoginAsync(loginRequest));
            Assert.Contains("suspended", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ForgotPasswordAsync_NonExistentEmail_DoesNotThrowAndDoesNotSendEmail()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            var authService = new AuthService(context, passwordHasher, jwtService, passwordResetService, mockEmailService.Object);

            // Act
            await authService.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "nonexistent@example.com" });

            // Assert
            mockEmailService.Verify(e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ForgotPasswordAndResetPassword_ValidFlow_ResetsPasswordSuccessfully()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            string? capturedToken = null;
            mockEmailService
                .Setup(e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((email, token) => capturedToken = token)
                .Returns(Task.CompletedTask);

            var authService = new AuthService(context, passwordHasher, jwtService, passwordResetService, mockEmailService.Object);

            var registerRequest = new RegisterRequestDto
            {
                FirstName = "Reset",
                LastName = "User",
                Email = "reset@example.com",
                Password = "OldPassword123!"
            };
            await authService.RegisterAsync(registerRequest);

            // Act 1: Forgot Password
            await authService.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "reset@example.com" });

            Assert.NotNull(capturedToken);

            // Verify raw token is NOT stored in DB
            var dbToken = await context.PasswordResetTokens.FirstOrDefaultAsync();
            Assert.NotNull(dbToken);
            Assert.NotEqual(capturedToken, dbToken.TokenHash);

            // Act 2: Reset Password
            var resetRequest = new ResetPasswordRequestDto
            {
                Email = "reset@example.com",
                Token = capturedToken,
                NewPassword = "NewPassword123!"
            };
            await authService.ResetPasswordAsync(resetRequest);

            // Act 3: Login with new password
            var loginResponse = await authService.LoginAsync(new LoginRequestDto
            {
                Email = "reset@example.com",
                Password = "NewPassword123!"
            });

            // Assert
            Assert.NotNull(loginResponse);
            Assert.Equal("reset@example.com", loginResponse.User.Email);
        }

        [Fact]
        public async Task ChangePasswordAsync_ValidCurrentPassword_UpdatesPassword()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            var authService = new AuthService(context, passwordHasher, jwtService, passwordResetService, mockEmailService.Object);

            var reg = await authService.RegisterAsync(new RegisterRequestDto
            {
                FirstName = "Change",
                LastName = "Pass",
                Email = "change@example.com",
                Password = "InitialPassword123!"
            });

            // Act
            await authService.ChangePasswordAsync(reg.UserId, new ChangePasswordRequestDto
            {
                CurrentPassword = "InitialPassword123!",
                NewPassword = "UpdatedPassword123!"
            });

            // Assert login with new password works
            var loginRes = await authService.LoginAsync(new LoginRequestDto
            {
                Email = "change@example.com",
                Password = "UpdatedPassword123!"
            });

            Assert.NotNull(loginRes);
        }
    }
}
