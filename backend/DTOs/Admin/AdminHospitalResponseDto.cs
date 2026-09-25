using System;

namespace LifeLink.DTOs.Admin
{
    public class AdminHospitalResponseDto
    {
        public Guid HospitalId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string LicenseNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public bool AwaitingAdminReview { get; set; } // rejected, and the hospital replied last
        public DateTime? ApprovedAt { get; set; }
        public Guid? ApprovedByAdminId { get; set; }
        public string? RejectionReason { get; set; }
        public string? RejectionReportUrl { get; set; }
        public string? RejectionReportName { get; set; }
        public string? RegistrationNumber { get; set; }
        public string? City { get; set; }
        public string? ContactPersonName { get; set; }
        public string? ContactPersonPhone { get; set; }
        public string? ContactPersonEmail { get; set; }
        public string? LicenseDocumentUrl { get; set; }
        public string? LicenseDocumentName { get; set; }
        public string? AccreditationDocumentUrl { get; set; }
        public string? AccreditationDocumentName { get; set; }
        public bool IsSuspended { get; set; }
        public DateTime? SuspendedUntil { get; set; }
        public string? SuspensionReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public System.Collections.Generic.List<LifeLink.DTOs.Hospitals.HospitalApprovalHistoryDto> ApprovalHistory { get; set; } = new();
    }
}
