using System;

namespace LifeLink.DTOs.BloodRequests
{
    public class CreateBloodRequestDto
    {
        public Guid HospitalId { get; set; }
        public string BloodGroup { get; set; } = string.Empty;
        public int UnitsRequired { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
    }
}
