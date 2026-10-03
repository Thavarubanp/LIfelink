using System;

namespace LifeLink.DTOs.BloodRequests
{
    public class BloodRequestResponseDto
    {
        public Guid BloodRequestId { get; set; }
        public Guid PatientUserId { get; set; }
        public Guid HospitalId { get; set; }
        public string BloodGroup { get; set; } = string.Empty;
        public int UnitsRequired { get; set; }
        public int FulfilledUnits { get; set; }
        public int ReservedUnits { get; set; }
        public int RemainingUnits => Math.Max(0, UnitsRequired - FulfilledUnits);
        // False while every remaining slot is reserved by an approved donor (request stays visible)
        public bool IsAcceptingDonors { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime ExpiryDate { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? DeletedAt { get; set; } // set when the creator deleted it (only the Admin still sees it)

        // Admin suspension (Phase 3B): while suspended nobody can act on it (see SuspensionGuard)
        public bool IsSuspended { get; set; }
        public DateTime? SuspendedAt { get; set; }
        public string? SuspensionReason { get; set; }
        public string? RejectionReason { get; set; }

        // Display details resolved from related records
        public string? HospitalName { get; set; }
        public string? CreatedByName { get; set; }
        public Guid? AssignedDoctorId { get; set; }
        public string? AssignedDoctorName { get; set; }

        // Hospital donation offers waiting for the assigned doctor's decision
        public int PendingHospitalDonations { get; set; }

        // Delete rules: blocked while a donor/hospital donation is active, and for good once a donor was screened
        public bool HasActiveAcceptances { get; set; }
        public bool HasScreenedDonors { get; set; }
    }
}
