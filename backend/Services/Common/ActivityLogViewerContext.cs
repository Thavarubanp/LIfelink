using System;
using System.Collections.Generic;

namespace LifeLink.Services.Common
{
    /// <summary>Server-derived identity used only to authorize activity-log enrichment.</summary>
    public sealed record ActivityLogViewerContext(Guid UserId, IReadOnlySet<string> Roles, Guid? HospitalId)
    {
        public bool IsAdmin => Roles.Contains("Admin");
        public bool IsHospitalStaff => Roles.Contains("HospitalStaff");
        public bool IsDoctor => Roles.Contains("Doctor");
        public bool IsUser => Roles.Contains("User");
    }
}
