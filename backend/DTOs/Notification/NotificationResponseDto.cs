using System;

namespace LifeLink.DTOs.Notification
{
    public class NotificationResponseDto
    {
        public Guid NotificationId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? HospitalId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string NotificationType { get; set; } = string.Empty;
        public string RecipientRole { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
