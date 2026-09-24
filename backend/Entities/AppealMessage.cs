using System;

namespace LifeLink.Entities
{
    /// <summary>
    /// One message in an appeal thread. AdminId set = admin reply; null = the appellant.
    /// Attachments follow the complaint reply pattern (data URL + file name).
    /// </summary>
    public class AppealMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public Guid AppealId { get; set; }
        public Guid? AdminId { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string? AttachmentName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Appeal Appeal { get; set; } = null!;
        public User? Admin { get; set; }
    }
}
