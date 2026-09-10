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

        // Navigation properties
        public User? User { get; set; }
        public Hospital? Hospital { get; set; }
    }
}
