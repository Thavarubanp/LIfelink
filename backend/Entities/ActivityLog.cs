using System;

namespace LifeLink.Entities
{
    /// <summary>
    /// One recorded action (append-only, no foreign keys so entries outlive retired accounts). Written in the same save as
    /// the action itself. ActorUserId is null for system actions; HospitalId is the hospital the entry belongs to (its
    /// staff, its doctors, or an admin action on it); SubjectUserId is the user an action was done to (e.g. a suspension).
    /// </summary>
    public class ActivityLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public Guid? ActorUserId { get; set; }
        public string ActorRole { get; set; } = string.Empty; // User, HospitalStaff, Doctor, Admin, System
        public string ActorName { get; set; } = string.Empty; // name at the time of the action
        public Guid? HospitalId { get; set; }
        public Guid? SubjectUserId { get; set; }
        public string Action { get; set; } = string.Empty;     // e.g. "BloodRequest.Deleted"
        public string EntityType { get; set; } = string.Empty; // the filter category, e.g. "BloodRequest"
        public Guid? EntityId { get; set; }
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>When an admin last opened an area of the Activity log page (drives the "new" highlight and badge).</summary>
    public class AdminSeenMarker
    {
        public Guid AdminUserId { get; set; }
        public string Area { get; set; } = string.Empty;
        public DateTime SeenAt { get; set; }
    }
}
