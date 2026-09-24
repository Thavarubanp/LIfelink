using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Auth;
using LifeLink.Entities;
using LifeLink.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Auth
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly IJwtService _jwtService;
        private readonly IPasswordResetService _passwordResetService;
        private readonly IEmailService _emailService;
        private readonly Microsoft.Extensions.Logging.ILogger<AuthService> _logger;

        public AuthService(
            AppDbContext context,
            IPasswordHasherService passwordHasher,
            IJwtService jwtService,
            IPasswordResetService passwordResetService,
            IEmailService emailService,
            Microsoft.Extensions.Logging.ILogger<AuthService>? logger = null)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _jwtService = jwtService;
            _passwordResetService = passwordResetService;
            _emailService = emailService;
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance;
        }

        public async Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            // Check global duplicate email across entire platform (User, Hospital, Doctor, Admin)
            if (await EmailUniquenessHelper.IsEmailTakenAsync(_context, normalizedEmail))
            {
                throw new InvalidOperationException("An account with this email address already exists.");
            }

            // Default role is 'User' (RoleId = 1)
            var defaultRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "User");
            if (defaultRole == null)
            {
                // Fallback in case roles table hasn't been seeded yet
                defaultRole = new Role { RoleId = 1, Name = "User" };
                _context.Roles.Add(defaultRole);
                await _context.SaveChangesAsync();
            }

            var newUser = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Email = normalizedEmail,
                PhoneNumber = request.PhoneNumber?.Trim() ?? string.Empty,
                DateOfBirth = request.DateOfBirth,
                Gender = request.Gender?.Trim() ?? string.Empty,
                Address = request.Address?.Trim() ?? string.Empty,
                AccountStatus = AccountStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            newUser.PasswordHash = _passwordHasher.HashPassword(newUser, request.Password);

            _context.Users.Add(newUser);

            _context.UserRoles.Add(new UserRole
            {
                UserId = newUser.UserId,
                RoleId = defaultRole.RoleId
            });

            await _context.SaveChangesAsync();

            return new RegisterResponseDto
            {
                UserId = newUser.UserId,
                FirstName = newUser.FirstName,
                LastName = newUser.LastName,
                Email = newUser.Email,
                AccountStatus = newUser.AccountStatus.ToString(),
                Message = "Registration successful."
            };
        }

        public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user == null)
            {
                throw new UnauthorizedAccessException("Invalid email or password.");
            }

            var isPasswordValid = _passwordHasher.VerifyPassword(user, user.PasswordHash, request.Password);
            if (!isPasswordValid)
            {
                throw new UnauthorizedAccessException("Invalid email or password.");
            }

            // Check Account Status (Note: Suspended accounts are allowed to authenticate in Restricted Governance Mode)
            if (user.AccountStatus == AccountStatus.Blocked)
            {
                throw new InvalidOperationException("This account has been permanently blocked.");
            }
            if (user.AccountStatus == AccountStatus.Inactive || user.AccountStatus == AccountStatus.Deleted)
            {
                throw new InvalidOperationException("Your account is inactive. Please contact support.");
            }
            if (user.AccountStatus == AccountStatus.Pending)
            {
                throw new InvalidOperationException("Your account is pending verification.");
            }

            var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
            if (!roles.Any())
            {
                roles.Add("User");
            }

            var (token, expiresAt) = _jwtService.GenerateToken(user, roles);
            var isSuspended = await GovernanceAccessHelper.IsSuspendedForAccessAsync(_context, user, roles);

            // Determine MustChangePassword for Doctor accounts
            bool mustChangePassword = false;
            if (roles.Contains("Doctor"))
            {
                var doctorRecord = await _context.Doctors
                    .FirstOrDefaultAsync(d => d.UserId == user.UserId);
                mustChangePassword = doctorRecord?.MustChangePassword ?? false;
            }

            return new LoginResponseDto
            {
                AccessToken = token,
                ExpiresAt = expiresAt,
                User = new CurrentUserDto
                {
                    UserId = user.UserId,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Email = user.Email,
                    Roles = roles,
                    AccountStatus = user.AccountStatus.ToString(),
                    IsSuspended = isSuspended,
                    MustChangePassword = mustChangePassword
                }
            };
        }

        public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                throw new KeyNotFoundException("User not found.");
            }

            // Inactive accounts cannot sign in; end any session still holding a token issued before deactivation
            if (user.AccountStatus is AccountStatus.Inactive or AccountStatus.Blocked or AccountStatus.Deleted)
            {
                throw new UnauthorizedAccessException("Your account is inactive. Please contact support.");
            }

            var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
            var isSuspended = await GovernanceAccessHelper.IsSuspendedForAccessAsync(_context, user, roles);

            // Enrich with MustChangePassword for Doctor accounts
            bool mustChangePassword = false;
            if (roles.Contains("Doctor"))
            {
                var doctorRecord = await _context.Doctors
                    .FirstOrDefaultAsync(d => d.UserId == userId);
                mustChangePassword = doctorRecord?.MustChangePassword ?? false;
            }

            return new CurrentUserDto
            {
                UserId = user.UserId,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Roles = roles,
                AccountStatus = user.AccountStatus.ToString(),
                IsSuspended = isSuspended,
                MustChangePassword = mustChangePassword
            };
        }

        private async Task<User?> FindOrEnsureUserByEmailAsync(string rawEmail)
        {
            if (string.IsNullOrWhiteSpace(rawEmail))
                return null;

            var normalizedEmail = rawEmail.Trim().ToLowerInvariant();

            // 1. Check in Users table (Donor, Patient, Admin, HospitalStaff, Doctor)
            var user = await _context.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user != null)
                return user;

            // 2. Check in Hospitals table (includes Pending, Approved, and Rejected hospitals)
            var hospital = await _context.Hospitals
                .FirstOrDefaultAsync(h => h.Email != null && h.Email.ToLower() == normalizedEmail);

            if (hospital != null)
            {
                user = new User
                {
                    UserId = Guid.NewGuid(),
                    FirstName = string.IsNullOrWhiteSpace(hospital.Name) ? "Hospital" : hospital.Name.Trim(),
                    LastName = "Staff",
                    Email = normalizedEmail,
                    PhoneNumber = hospital.ContactNumber?.Trim() ?? string.Empty,
                    Address = hospital.Address?.Trim() ?? string.Empty,
                    AccountStatus = AccountStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    PasswordHash = string.Empty
                };

                await _context.Users.AddAsync(user);

                var staffRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "HospitalStaff");
                int roleId = staffRole?.RoleId ?? 2;

                await _context.UserRoles.AddAsync(new UserRole
                {
                    UserId = user.UserId,
                    RoleId = roleId
                });

                await _context.SaveChangesAsync();
                return user;
            }

            // 3. Check in Doctors table
            var doctor = await _context.Doctors
                .FirstOrDefaultAsync(d => d.Email != null && d.Email.ToLower() == normalizedEmail);

            if (doctor != null)
            {
                user = new User
                {
                    UserId = Guid.NewGuid(),
                    FirstName = string.IsNullOrWhiteSpace(doctor.FirstName) ? "Dr." : doctor.FirstName.Trim(),
                    LastName = string.IsNullOrWhiteSpace(doctor.LastName) ? "Doctor" : doctor.LastName.Trim(),
                    Email = normalizedEmail,
                    PhoneNumber = doctor.PhoneNumber?.Trim() ?? string.Empty,
                    AccountStatus = AccountStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    PasswordHash = string.Empty
                };

                await _context.Users.AddAsync(user);

                var doctorRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "Doctor");
                int roleId = doctorRole?.RoleId ?? 3;

                await _context.UserRoles.AddAsync(new UserRole
                {
                    UserId = user.UserId,
                    RoleId = roleId
                });

                doctor.UserId = user.UserId;
                _context.Doctors.Update(doctor);

                await _context.SaveChangesAsync();
                return user;
            }

            return null;
        }

        public async Task DeleteMyAccountAsync(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId) ?? throw new KeyNotFoundException("User not found.");
            if (AccountLifecycleHelper.IsRemoved(user))
            {
                throw new InvalidOperationException("This account no longer exists.");
            }
            // Admin, HospitalStaff and Doctor accounts cannot self-delete
            await AccountLifecycleHelper.EnsureDonorPatientAccountAsync(_context, userId, "deleted by their owner");

            await AccountLifecycleHelper.DeleteAccountAsync(_context, user);
            await _context.SaveChangesAsync();
        }

        public async Task ForgotPasswordAsync(ForgotPasswordRequestDto request)
        {
            var user = await FindOrEnsureUserByEmailAsync(request.Email);

            // Anti-account enumeration: always return generic response if user doesn't exist
            if (user == null)
            {
                _logger.LogInformation("ForgotPassword: Email {Email} requested password reset, but account was not found (Anti-enumeration triggered).", request.Email);
                return;
            }

            var otp = await _passwordResetService.CreatePasswordResetOtpAsync(user);

            var targetEmail = !string.IsNullOrWhiteSpace(user.Email) ? user.Email.Trim() : request.Email.Trim();

            try
            {
                await _emailService.SendPasswordResetOtpAsync(targetEmail, otp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ForgotPassword: Failed to send OTP email to {Email}. Error: {Message}", targetEmail, ex.Message);
            }
        }

        public async Task<VerifyOtpResponseDto> VerifyOtpAsync(VerifyOtpRequestDto request)
        {
            var user = await FindOrEnsureUserByEmailAsync(request.Email);
            if (user == null)
            {
                throw new InvalidOperationException("Invalid or expired verification code.");
            }

            var verifiedToken = await _passwordResetService.VerifyOtpAsync(user, request.Otp);
            if (verifiedToken == null || string.IsNullOrEmpty(verifiedToken.ResetSessionToken))
            {
                throw new InvalidOperationException("Invalid or expired verification code. Please enter the correct code or request a new OTP.");
            }

            return new VerifyOtpResponseDto
            {
                Success = true,
                Message = "OTP verified successfully.",
                ResetSessionToken = verifiedToken.ResetSessionToken
            };
        }

        public async Task ResendOtpAsync(ResendOtpRequestDto request)
        {
            var user = await FindOrEnsureUserByEmailAsync(request.Email);
            if (user == null)
            {
                return; // Anti-enumeration
            }

            if (await _passwordResetService.IsResendOnCooldownAsync(user))
            {
                throw new InvalidOperationException("Please wait 30 seconds before requesting a new OTP.");
            }

            var otp = await _passwordResetService.CreatePasswordResetOtpAsync(user);

            var targetEmail = !string.IsNullOrWhiteSpace(user.Email) ? user.Email.Trim() : request.Email.Trim();

            try
            {
                await _emailService.SendPasswordResetOtpAsync(targetEmail, otp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ResendOtp: Failed to send OTP email to {Email}. Error: {Message}", targetEmail, ex.Message);
            }
        }

        public async Task ResetPasswordAsync(ResetPasswordRequestDto request)
        {
            var user = await FindOrEnsureUserByEmailAsync(request.Email);
            if (user == null)
            {
                throw new InvalidOperationException("Invalid password reset request.");
            }

            var validToken = await _passwordResetService.ValidateVerifiedResetTokenAsync(user, request.Token);
            if (validToken == null)
            {
                // Fallback attempt: if client passed raw OTP directly in token field
                validToken = await _passwordResetService.VerifyOtpAsync(user, request.Token);
            }

            if (validToken == null)
            {
                throw new InvalidOperationException("OTP verification is required before setting a new password, or your reset session has expired.");
            }

            // Hash new password using ASP.NET Core Identity PBKDF2
            user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            // Immediately mark token as used so it cannot be used again
            await _passwordResetService.MarkTokenAsUsedAsync(validToken);

            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }

        public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequestDto request)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
            {
                throw new KeyNotFoundException("User not found.");
            }

            var isCurrentPasswordValid = _passwordHasher.VerifyPassword(user, user.PasswordHash, request.CurrentPassword);
            if (!isCurrentPasswordValid)
            {
                throw new InvalidOperationException("Current password is incorrect.");
            }

            user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            _context.Users.Update(user);

            // If this user is a Doctor with a pending first-login password change,
            // automatically clear the flag so they can access the dashboard normally.
            var isDoctor = user.UserRoles.Any(ur => ur.Role.Name == "Doctor");
            if (isDoctor)
            {
                var doctorRecord = await _context.Doctors
                    .FirstOrDefaultAsync(d => d.UserId == userId);
                if (doctorRecord != null && doctorRecord.MustChangePassword)
                {
                    doctorRecord.MustChangePassword = false;
                    doctorRecord.UpdatedAt = DateTime.UtcNow;
                    _context.Doctors.Update(doctorRecord);
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task<UserScreeningProfileDto?> GetUserScreeningProfileAsync(Guid userId, bool includeScreeningPrefill = false)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return null;

            var dto = new UserScreeningProfileDto
            {
                UserId = user.UserId,
                FullName = $"{user.FirstName} {user.LastName}".Trim(),
                Gender = user.Gender,
                DateOfBirth = user.DateOfBirth
            };

            // Contact details pre-fill screening Section 1 for the donor to confirm; only the screening agent gets them
            if (includeScreeningPrefill)
            {
                dto.Email = user.Email;
                dto.PhoneNumber = user.PhoneNumber;
                dto.Address = user.Address;
                dto.BloodGroup = user.BloodGroup;
                dto.LastDonationDate = user.LastDonationDate;
            }

            return dto;
        }
    }
}
