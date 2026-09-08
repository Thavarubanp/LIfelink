using System;

namespace LifeLink.DTOs.Emergency
{
    public class EmergencyRequestResponseDto
    {
        public Guid EmergencyRequestId { get; set; }
        public Guid HospitalId { get; set; }
        public string HospitalName { get; set; } = string.Empty;
        public string BloodGroup { get; set; } = string.Empty;
        public int UnitsRequired { get; set; }
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
