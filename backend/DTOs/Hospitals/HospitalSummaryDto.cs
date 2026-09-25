using System;

namespace LifeLink.DTOs.Hospitals
{
    /// <summary>
    /// Hospital directory entry for signed-in users (hospital pickers, dashboards). It carries no documents,
    /// registration conversation or authorized-person details.
    /// </summary>
    public class HospitalSummaryDto
    {
        public Guid HospitalId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? City { get; set; }
        public string Address { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public string LicenseNumber { get; set; } = string.Empty;
        public string? RegistrationNumber { get; set; }
        public bool IsVerified { get; set; }
        public bool IsSuspended { get; set; } // suspended hospitals are hidden from operational hospital pickers
        public DateTime CreatedAt { get; set; }
    }
}
