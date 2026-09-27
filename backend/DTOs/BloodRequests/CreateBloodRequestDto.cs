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

        // Hospital staff only (mandatory for them): one of the hospital's own doctors, who approves the request and
        // any hospital donation to it. Ignored for donor/patient and Admin requests (the hospital assigns a doctor).
        public Guid? DoctorId { get; set; }
    }
}
