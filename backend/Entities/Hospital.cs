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
        public string? RejectionReportUrl { get; set; }
        public string? RejectionReportName { get; set; }
        public bool IsSuspended { get; set; } = false;
        public DateTime? SuspendedUntil { get; set; }
        public string? SuspensionReason { get; set; }
        public bool IsPermanentlyBlocked { get; set; } = false;

        // Blood packet settings: shelf life of collected packets (21-35 days) and the "going to expire" window
        public int PacketShelfLifeDays { get; set; } = 35;
        public int ExpiryAlertDays { get; set; } = 5;

        // Registration details & Documents
        public string? RegistrationNumber { get; set; }
        public string? City { get; set; }
        public string? ContactPersonName { get; set; }
        public string? ContactPersonPhone { get; set; }
        public string? ContactPersonEmail { get; set; }
        public string? LicenseDocumentUrl { get; set; }
        public string? LicenseDocumentName { get; set; }
        public string? AccreditationDocumentUrl { get; set; }
        public string? AccreditationDocumentName { get; set; }
        // Legacy fields of the retired resubmission workflow; no longer written (the registration conversation records replies)
        public DateTime? ResubmittedAt { get; set; }
        public string? UpdatedFields { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public User? ApprovedByAdmin { get; set; }
        public ICollection<Doctor> Doctors { get; set; } = new List<Doctor>();
        public ICollection<BloodInventory> BloodInventories { get; set; } = new List<BloodInventory>();
        public ICollection<EmergencyRequest> EmergencyRequests { get; set; } = new List<EmergencyRequest>();
        public ICollection<HospitalTransferRequest> SentTransferRequests { get; set; } = new List<HospitalTransferRequest>();
        public ICollection<HospitalTransferRequest> ReceivedTransferRequests { get; set; } = new List<HospitalTransferRequest>();
        public ICollection<HospitalApprovalHistory> ApprovalHistories { get; set; } = new List<HospitalApprovalHistory>();
    }
}
