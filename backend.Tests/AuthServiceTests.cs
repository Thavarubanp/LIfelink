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
        public async Task LoginAsync_SuspendedAccount_AllowsLoginInRestrictedGovernanceMode()
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
                IsSuspended = true,
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

            // Act - Under Restricted Governance Mode, authentication succeeds and issues JWT
            var result = await authService.LoginAsync(loginRequest);

            // Assert
            Assert.NotNull(result);
            Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
            Assert.Equal("Suspended", result.User.AccountStatus);
        }

        [Fact]
        public async Task ForgotPasswordAsync_NonExistentEmail_DoesNotThrowAndDoesNotSendOtp()
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
            mockEmailService.Verify(e => e.SendPasswordResetOtpAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ForgotPasswordAndResetPassword_OtpFlow_ResetsPasswordSuccessfully()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            string? capturedOtp = null;
            mockEmailService
                .Setup(e => e.SendPasswordResetOtpAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((email, otp) => capturedOtp = otp)
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

            // Act 1: Forgot Password (generates 6-digit OTP)
            await authService.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "reset@example.com" });

            Assert.NotNull(capturedOtp);
            Assert.Equal(6, capturedOtp.Length);

            // Act 2: Verify OTP
            var verifyRes = await authService.VerifyOtpAsync(new VerifyOtpRequestDto
            {
                Email = "reset@example.com",
                Otp = capturedOtp
            });

            Assert.NotNull(verifyRes);
            Assert.True(verifyRes.Success);
            Assert.False(string.IsNullOrWhiteSpace(verifyRes.ResetSessionToken));

            // Act 3: Reset Password using resetSessionToken
            var resetRequest = new ResetPasswordRequestDto
            {
                Email = "reset@example.com",
                Token = verifyRes.ResetSessionToken,
                NewPassword = "NewPassword123!"
            };
            await authService.ResetPasswordAsync(resetRequest);

            // Act 4: Login with new password
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

        [Fact]
        public async Task ForgotPasswordAsync_AdminAccount_GeneratesOtpAndDispatchesEmail()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            string? sentEmail = null;
            string? sentOtp = null;
            mockEmailService
                .Setup(e => e.SendPasswordResetOtpAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((e, otp) => { sentEmail = e; sentOtp = otp; })
                .Returns(Task.CompletedTask);

            var authService = new AuthService(context, passwordHasher, jwtService, passwordResetService, mockEmailService.Object);

            // Create Admin user in DB
            var adminRole = context.Roles.First(r => r.Name == "Admin");
            var adminUser = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = "Admin",
                LastName = "Super",
                Email = "admin@lifelink.org",
                AccountStatus = AccountStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            adminUser.PasswordHash = passwordHasher.HashPassword(adminUser, "AdminPass123!");
            context.Users.Add(adminUser);
            context.UserRoles.Add(new UserRole { UserId = adminUser.UserId, RoleId = adminRole.RoleId });
            await context.SaveChangesAsync();

            // Act
            await authService.ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = "admin@lifelink.org" });

            // Assert
            Assert.NotNull(sentOtp);
            Assert.Equal(6, sentOtp.Length);
            Assert.Equal("admin@lifelink.org", sentEmail);

            // Verify OTP works for reset session token
            var verifyRes = await authService.VerifyOtpAsync(new VerifyOtpRequestDto
            {
                Email = "admin@lifelink.org",
                Otp = sentOtp
            });
            Assert.NotNull(verifyRes);
            Assert.True(verifyRes.Success);
            Assert.False(string.IsNullOrWhiteSpace(verifyRes.ResetSessionToken));
        }

        [Fact]
        public async Task RegisterAsync_EmailExistsAsHospital_ThrowsInvalidOperationException()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            context.Hospitals.Add(new Hospital
            {
                HospitalId = Guid.NewGuid(),
                Name = "City General",
                Email = "hospital@lifelink.org",
                ContactNumber = "123456"
            });
            await context.SaveChangesAsync();

            var authService = new AuthService(context, passwordHasher, jwtService, passwordResetService, mockEmailService.Object);

            var req = new RegisterRequestDto
            {
                FirstName = "John",
                LastName = "Doe",
                Email = "hospital@lifelink.org",
                Password = "Password123!"
            };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.RegisterAsync(req));
            Assert.Equal("An account with this email address already exists.", ex.Message);
        }

        [Fact]
        public async Task RegisterAsync_EmailExistsAsDoctor_ThrowsInvalidOperationException()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            context.Doctors.Add(new Doctor
            {
                DoctorId = Guid.NewGuid(),
                HospitalId = Guid.NewGuid(),
                FirstName = "Gregory",
                LastName = "House",
                Email = "doctor@lifelink.org"
            });
            await context.SaveChangesAsync();

            var authService = new AuthService(context, passwordHasher, jwtService, passwordResetService, mockEmailService.Object);

            var req = new RegisterRequestDto
            {
                FirstName = "Jane",
                LastName = "Doe",
                Email = "doctor@lifelink.org",
                Password = "Password123!"
            };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.RegisterAsync(req));
            Assert.Equal("An account with this email address already exists.", ex.Message);
        }
    }
}
