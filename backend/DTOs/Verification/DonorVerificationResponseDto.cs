using System;

namespace LifeLink.DTOs.Verification
{
    /// <summary>One screening report version and its doctor decision.</summary>
    public class DonorVerificationResponseDto
    {
        public Guid DonorVerificationId { get; set; }
        public Guid AcceptanceId { get; set; }
        public int ReportVersion { get; set; }
        public bool IsLatestVersion { get; set; }
        public Guid? DoctorId { get; set; } // assigned reviewer; null when the doctor account was deleted
        public string DoctorName { get; set; } = string.Empty;
        public Guid? DecidedByDoctorId { get; set; }
        public string? DecidedByName { get; set; }
        public bool IsAssignedToMe { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? MedicalReportSummary { get; set; }
        public string? ReportJson { get; set; }
        public string? RiskLevel { get; set; }
        public string? Recommendation { get; set; }
        public string? Notes { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        // Donor and request context
        public Guid? DonorUserId { get; set; }
        public string? DonorName { get; set; }
        public string? DonorBloodGroup { get; set; }
        public string? DonorAccountStatus { get; set; }
        public string? AcceptanceStatus { get; set; }
        public Guid? BloodRequestId { get; set; }
        public string? RequestBloodGroup { get; set; }
        public string? RequestStatus { get; set; }
        public int UnitsRequired { get; set; }
        public int FulfilledUnits { get; set; }
        public int ReservedUnits { get; set; }
        public bool HasFreeSlot { get; set; }
    }
}
