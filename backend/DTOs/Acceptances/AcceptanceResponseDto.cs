using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.Acceptances
{
    public class AcceptanceResponseDto
    {
        public Guid AcceptanceId { get; set; }
        public Guid BloodRequestId { get; set; }
        public Guid DonorUserId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime AcceptedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? RejectionReason { get; set; }

        // Request context (filled for the donor's own list)
        public Guid? HospitalId { get; set; }
        public string? HospitalName { get; set; }
        public string? RequestBloodGroup { get; set; }
        public string? RequestPriority { get; set; }
        public string? RequestStatus { get; set; }
        public int UnitsRequired { get; set; }
        public int FulfilledUnits { get; set; }
        public int ReservedUnits { get; set; }

        // Every screening report version with its doctor decision (report content is not repeated here)
        public List<ScreeningDecisionDto> ScreeningHistory { get; set; } = new();
    }

    public class ScreeningDecisionDto
    {
        public Guid DonorVerificationId { get; set; }
        public int ReportVersion { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecidedByName { get; set; }
        public string? ApprovalNotes { get; set; }
        public string? RejectionReason { get; set; }
        public string? Note { get; set; } // why a version was superseded or closed
    }
}
