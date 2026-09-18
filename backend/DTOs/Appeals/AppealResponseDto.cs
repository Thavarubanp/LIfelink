using System;

namespace LifeLink.DTOs.Appeals
{
    public class AppealResponseDto
    {
        public Guid AppealId { get; set; }
        public Guid? UserId { get; set; }
        public string? UserEmail { get; set; }
        public Guid? HospitalId { get; set; }
        public string? HospitalName { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public Guid? ReviewedByAdminId { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? AdminResponse { get; set; }
    }
}
