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

        public async Task<string> CreatePasswordResetTokenAsync(User user)
        {
            // Generate cryptographically secure 32-byte random token
            var randomBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }

            var rawToken = Convert.ToBase64String(randomBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");

            var tokenHash = HashToken(rawToken);

            // Invalidate old unused tokens for this user
            var existingTokens = await _context.PasswordResetTokens
                .Where(t => t.UserId == user.UserId && t.UsedAt == null)
                .ToListAsync();

            foreach (var oldToken in existingTokens)
            {
                oldToken.UsedAt = DateTime.UtcNow; // Mark old tokens as invalid/used
            }

            var resetToken = new PasswordResetToken
            {
                PasswordResetTokenId = Guid.NewGuid(),
                UserId = user.UserId,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15), // 15 minutes validity
                CreatedAt = DateTime.UtcNow,
                UsedAt = null
            };

            _context.PasswordResetTokens.Add(resetToken);
            await _context.SaveChangesAsync();

            return rawToken;
        }

        public async Task<PasswordResetToken?> ValidateTokenAsync(User user, string rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
                return null;

            var tokenHash = HashToken(rawToken);

            var token = await _context.PasswordResetTokens
                .FirstOrDefaultAsync(t => t.UserId == user.UserId && t.TokenHash == tokenHash);

            if (token == null)
                return null;

            // Check expiration and single-use condition
            if (token.ExpiresAt < DateTime.UtcNow || token.UsedAt != null)
                return null;

            return token;
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
