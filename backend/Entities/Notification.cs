using System;

namespace LifeLink.Entities
{
    public class Notification
    {
        public Guid NotificationId { get; set; } = Guid.NewGuid();
        public Guid? UserId { get; set; }
        public Guid? HospitalId { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string NotificationType { get; set; } = string.Empty;
        public string RecipientRole { get; set; } = string.Empty;
        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        /// <summary>Set when the recipient dismisses it; it is then hidden from their lists and counts.</summary>
        public DateTime? DismissedAt { get; set; }
        /// <summary>
        /// Stable key of an inventory alert (type + blood group, plus the low hospital for a "help" alert). The same unread
        /// alert with the same key is not sent to the same hospital again within 12 hours (Q12: AI-rewritten titles change).
        /// </summary>
        public string? DedupeKey { get; set; }

        // Navigation properties
        public User? User { get; set; }
        public Hospital? Hospital { get; set; }
    }
}
