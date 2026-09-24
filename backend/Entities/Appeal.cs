using System;

namespace LifeLink.Entities
{
    public class Appeal
    {
        public Guid AppealId { get; set; } = Guid.NewGuid();
        public Guid? UserId { get; set; }
        public Guid? HospitalId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public AppealStatus Status { get; set; } = AppealStatus.PENDING;
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public Guid? ReviewedByAdminId { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? AdminResponse { get; set; }

        // Navigation properties
        public User? User { get; set; }
        public Hospital? Hospital { get; set; }
        public User? ReviewedByAdmin { get; set; }
        public System.Collections.Generic.ICollection<AppealMessage> Messages { get; set; } = new System.Collections.Generic.List<AppealMessage>();
    }
}
