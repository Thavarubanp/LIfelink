using System;
using System.Collections.Generic;
using LifeLink.DTOs.Appeals;

namespace LifeLink.DTOs.Governance
{
    public class GovernanceStatusDto
    {
        public bool IsSuspended { get; set; }
        public string? SuspensionReason { get; set; }
        public DateTime? SuspendedUntil { get; set; }
        public bool IsPermanentlyBlocked { get; set; }
        public string? AppealStatus { get; set; }
        public bool HasPendingAppeal { get; set; }
        public string SuspendedEntity { get; set; } = string.Empty; // "User", "Hospital" or "None"
        public List<string> AllowedActions { get; set; } = new List<string>();
        // Full appeal history (each appeal carries its message thread), ordered oldest -> newest
        public List<AppealResponseDto> AllAppeals { get; set; } = new List<AppealResponseDto>();

        // Governance Portal
        public GovernanceProfileSummaryDto Profile { get; set; } = new();
        public bool CanAppeal { get; set; }       // may open a new appeal (suspended, no open thread, not a doctor)
        public bool IsReadOnlyViewer { get; set; } // doctors of a suspended hospital: view only
    }

    public class GovernanceProfileSummaryDto
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? HospitalName { get; set; }
    }
}
