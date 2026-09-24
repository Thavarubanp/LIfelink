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
        public string? RejectionReason { get; set; }

        // Display details resolved from related records
        public string? HospitalName { get; set; }
        public string? CreatedByName { get; set; }
        public Guid? AssignedDoctorId { get; set; }
        public string? AssignedDoctorName { get; set; }
    }
}
