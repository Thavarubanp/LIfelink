using System;

namespace LifeLink.Entities
{
    public class Appeal : IConcurrencyVersioned
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
        // Set by the first rejection; an appeal can be rejected only once
        public DateTime? RejectedAt { get; set; }
        // Bumped on every change to the appeal or a new message in its thread (AppDbContext), so a reply and an admin
        // decision/reinstatement at the same moment cannot both succeed: the later save fails (409)
        public int ConcurrencyToken { get; set; }

        // Navigation properties
        public User? User { get; set; }
        public Hospital? Hospital { get; set; }
        public User? ReviewedByAdmin { get; set; }
        public System.Collections.Generic.ICollection<AppealMessage> Messages { get; set; } = new System.Collections.Generic.List<AppealMessage>();
    }
}
