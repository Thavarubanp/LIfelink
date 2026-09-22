using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Auth
{
    public class PasswordResetService : IPasswordResetService
    {
        private readonly AppDbContext _context;

        public PasswordResetService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<string> CreatePasswordResetOtpAsync(User user)
        {
            // 1. Generate secure 6-digit numeric OTP (e.g. 100000 - 999999)
            int otpValue = RandomNumberGenerator.GetInt32(100000, 1000000);
            string otp = otpValue.ToString("D6");

            // 2. Invalidate ALL previous active unused tokens / OTPs for this user
            var existingActiveTokens = await _context.PasswordResetTokens
                .Where(t => t.UserId == user.UserId && t.UsedAt == null)
                .ToListAsync();

            foreach (var oldToken in existingActiveTokens)
            {
                oldToken.UsedAt = DateTime.UtcNow; // Immediately invalidate old OTPs
            }

            // 3. Create fresh OTP token with 10-minute expiry
            var resetToken = new PasswordResetToken
            {
                PasswordResetTokenId = Guid.NewGuid(),
                UserId = user.UserId,
                TokenHash = HashToken(otp),
                Otp = otp,
                IsVerified = false,
                ResetSessionToken = null,
                LastSentAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10), // 10-minute expiry
                CreatedAt = DateTime.UtcNow,
                UsedAt = null
            };

            _context.PasswordResetTokens.Add(resetToken);
            await _context.SaveChangesAsync();

            return otp;
        }

        public async Task<bool> IsResendOnCooldownAsync(User user)
        {
            var recentToken = await _context.PasswordResetTokens
                .Where(t => t.UserId == user.UserId && t.LastSentAt != null)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (recentToken?.LastSentAt != null)
            {
                var elapsedSeconds = (DateTime.UtcNow - recentToken.LastSentAt.Value).TotalSeconds;
                if (elapsedSeconds < 30) // 30-second cooldown between resends
                {
                    return true;
                }
            }

            return false;
        }

        public async Task<PasswordResetToken?> VerifyOtpAsync(User user, string otp)
        {
            if (string.IsNullOrWhiteSpace(otp))
                return null;

            var cleanOtp = otp.Trim();

            // Find active unexpired token for this user matching OTP
            var token = await _context.PasswordResetTokens
                .Where(t => t.UserId == user.UserId && t.UsedAt == null)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (token == null)
                return null;

            // Check expiry & OTP code match
            if (token.ExpiresAt < DateTime.UtcNow || token.Otp != cleanOtp)
                return null;

            // Generate cryptographically secure single-use resetSessionToken
            var sessionBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(sessionBytes);
            }
            string resetSessionToken = Convert.ToBase64String(sessionBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");

            token.IsVerified = true;
            token.ResetSessionToken = resetSessionToken;
            _context.PasswordResetTokens.Update(token);
            await _context.SaveChangesAsync();

            return token;
        }

        public async Task<PasswordResetToken?> ValidateVerifiedResetTokenAsync(User user, string resetSessionToken)
        {
            if (string.IsNullOrWhiteSpace(resetSessionToken))
                return null;

            var token = await _context.PasswordResetTokens
                .FirstOrDefaultAsync(t => t.UserId == user.UserId && t.ResetSessionToken == resetSessionToken && t.IsVerified);

            if (token == null)
                return null;

            if (token.ExpiresAt < DateTime.UtcNow || token.UsedAt != null)
                return null;

            return token;
        }

        public async Task<string> CreatePasswordResetTokenAsync(User user)
        {
            return await CreatePasswordResetOtpAsync(user);
        }

        public async Task<PasswordResetToken?> ValidateTokenAsync(User user, string rawToken)
        {
            // Allow validation by session token or OTP
            var verified = await ValidateVerifiedResetTokenAsync(user, rawToken);
            if (verified != null) return verified;

            return await VerifyOtpAsync(user, rawToken);
        }

        public async Task MarkTokenAsUsedAsync(PasswordResetToken token)
        {
            token.UsedAt = DateTime.UtcNow;
            _context.PasswordResetTokens.Update(token);
            await _context.SaveChangesAsync();
        }

        public string HashToken(string rawToken)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes);
        }
    }
}
