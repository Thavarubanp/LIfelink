using System;

namespace LifeLink.DTOs.Verification
{
    public class ApproveRejectRequestDto
    {
        public Guid DoctorId { get; set; }
        public string? Notes { get; set; }
        public string? MedicalReportSummary { get; set; }
        public string? Priority { get; set; } = "Normal";
        public string? BloodGroup { get; set; }
        public Guid? HospitalId { get; set; }
    }
}
