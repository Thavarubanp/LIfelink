using System;
using System.Collections.Generic;

namespace LifeLink.Entities
{
    public class Hospital
    {
        public Guid HospitalId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string LicenseNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsVerified { get; set; } = false;

        // Student 4 Additions: Governance & Compliance
        public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Pending;
        public DateTime? ApprovedAt { get; set; }
        public Guid? ApprovedByAdminId { get; set; }
        public string? RejectionReason { get; set; }
        public bool IsSuspended { get; set; } = false;
        public DateTime? SuspendedUntil { get; set; }
        public string? SuspensionReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public User? ApprovedByAdmin { get; set; }
        public ICollection<Doctor> Doctors { get; set; } = new List<Doctor>();
        public ICollection<BloodInventory> BloodInventories { get; set; } = new List<BloodInventory>();
        public ICollection<EmergencyRequest> EmergencyRequests { get; set; } = new List<EmergencyRequest>();
        public ICollection<HospitalTransferRequest> SentTransferRequests { get; set; } = new List<HospitalTransferRequest>();
        public ICollection<HospitalTransferRequest> ReceivedTransferRequests { get; set; } = new List<HospitalTransferRequest>();
    }
}
