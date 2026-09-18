using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Auth;
using LifeLink.Entities;
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

        public AuthService(
            AppDbContext context,
            IPasswordHasherService passwordHasher,
            IJwtService jwtService,
            IPasswordResetService passwordResetService,
            IEmailService emailService)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _jwtService = jwtService;
            _passwordResetService = passwordResetService;
            _emailService = emailService;
        }

        public async Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            // Check duplicate email
            var existingUser = await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail);
            if (existingUser)
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
            if (user.AccountStatus == AccountStatus.Inactive)
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
                    IsSuspended = user.IsSuspended
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

            var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();

            return new CurrentUserDto
            {
                UserId = user.UserId,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Roles = roles,
                AccountStatus = user.AccountStatus.ToString(),
                IsSuspended = user.IsSuspended
            };
        }

        public async Task ForgotPasswordAsync(ForgotPasswordRequestDto request)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            // Anti-account enumeration: do not reveal if user does not exist
            if (user == null)
            {
                return;
            }

            var resetToken = await _passwordResetService.CreatePasswordResetTokenAsync(user);
            await _emailService.SendPasswordResetEmailAsync(user.Email, resetToken);
        }

        public async Task ResetPasswordAsync(ResetPasswordRequestDto request)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
            if (user == null)
            {
                throw new InvalidOperationException("Invalid password reset request.");
            }

            var validToken = await _passwordResetService.ValidateTokenAsync(user, request.Token);
            if (validToken == null)
            {
                throw new InvalidOperationException("Invalid or expired password reset token.");
            }

            // Hash new password
            user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            await _passwordResetService.MarkTokenAsUsedAsync(validToken);

            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }

        public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequestDto request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
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
            await _context.SaveChangesAsync();
        }

        public async Task<UserScreeningProfileDto?> GetUserScreeningProfileAsync(Guid userId)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return null;

            return new UserScreeningProfileDto
            {
                UserId = user.UserId,
                FullName = $"{user.FirstName} {user.LastName}".Trim(),
                Gender = user.Gender,
                DateOfBirth = user.DateOfBirth
            };
        }
    }
}
