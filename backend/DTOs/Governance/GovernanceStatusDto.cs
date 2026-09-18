using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.Governance
{
    public class GovernanceStatusDto
    {
        public bool IsSuspended { get; set; }
        public string? SuspensionReason { get; set; }
        public DateTime? SuspendedUntil { get; set; }
        public string? AppealStatus { get; set; }
        public bool HasPendingAppeal { get; set; }
        public string SuspendedEntity { get; set; } = string.Empty;
        public List<string> AllowedActions { get; set; } = new List<string>();
    }
}
