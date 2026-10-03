using System;

namespace LifeLink.Entities
{
    public class BloodRequest : IConcurrencyVersioned
    {
        public Guid BloodRequestId { get; set; } = Guid.NewGuid();
        public Guid PatientUserId { get; set; }
        public Guid HospitalId { get; set; }
        public string BloodGroup { get; set; } = string.Empty;
        public int UnitsRequired { get; set; }
        public int FulfilledUnits { get; set; } = 0; // donations actually recorded
        public int ReservedUnits { get; set; } = 0;  // doctor-approved donors who have not donated yet
        public int ConcurrencyToken { get; set; } = 0;
        public string Reason { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public BloodRequestStatus Status { get; set; } = BloodRequestStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiryDate { get; set; }
        public DateTime? CancelledAt { get; set; }

        /// <summary>Set when the creator deletes the request (Status = Deleted). Nothing is removed; only the Admin still sees it.</summary>
        public DateTime? DeletedAt { get; set; }

        /// <summary>
        /// Message shown to the creator when the hospital, the assigned doctor, or expiry rejects the request.
        /// </summary>
        public string? RejectionReason { get; set; }

        /// <summary>
        /// Admin suspension (Phase 3B): while set, nobody can act on it (refused with 409), except the Q7 exceptions for
        /// blood requests (a donor withdrawing and the creator deleting). Cleared when the admin lifts it.
        /// </summary>
        public DateTime? AdminSuspendedAt { get; set; }
        public Guid? AdminSuspendedByUserId { get; set; }
        public string? AdminSuspensionReason { get; set; }
    }
}
