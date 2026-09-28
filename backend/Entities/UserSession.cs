using System;

namespace LifeLink.Entities
{
    /// <summary>
    /// One signed-in session (one login). The access token carries its id in the "sid" claim. A session ends on
    /// sign-out, or when the user has been idle longer than Session:IdleTimeoutMinutes; after that its token is refused.
    /// </summary>
    public class UserSession
    {
        public Guid SessionId { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
        public DateTime? EndedAt { get; set; }
        public string? EndReason { get; set; } // SignedOut, Idle

        public User? User { get; set; }
    }

    public static class SessionEndReasons
    {
        public const string SignedOut = "SignedOut";
        public const string Idle = "Idle";
    }
}
