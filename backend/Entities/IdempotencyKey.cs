using System;

namespace LifeLink.Entities
{
    /// <summary>
    /// A client-generated key for one form submission (header Idempotency-Key). It is saved in the same save as the
    /// records the action creates, so a double click or a retry with the same key cannot create them twice.
    /// </summary>
    public class IdempotencyKey
    {
        public string Key { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
