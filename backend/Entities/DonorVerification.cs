using System;

namespace LifeLink.Entities
{
    public class DonorVerification
    {
        public Guid DonorVerificationId { get; set; } = Guid.NewGuid();
        public Guid AcceptanceId { get; set; }
        // Nullable so screening history survives when the hospital deletes the doctor
        public Guid? DoctorId { get; set; }

        public VerificationStatus Status { get; set; } = VerificationStatus.Pending;
        // Screening report version: ReportJson/MedicalReportSummary are immutable once submitted (enforced in AppDbContext)
        public int ReportVersion { get; set; } = 1;
        public string? ReportJson { get; set; }
        public string? MedicalReportSummary { get; set; }
        // Doctor who made the decision (the assigned doctor, or a same-hospital fallback); Notes hold approval notes or the rejection reason
        public Guid? DecidedByDoctorId { get; set; }
        public string? Notes { get; set; }
        public DateTime? VerifiedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Doctor? Doctor { get; set; }
        public Doctor? DecidedByDoctor { get; set; }
    }
}
