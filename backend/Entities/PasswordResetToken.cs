using System;

namespace LifeLink.Entities
{
    public class PasswordResetToken
    {
        public Guid PasswordResetTokenId { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User User { get; set; } = null!;

        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
