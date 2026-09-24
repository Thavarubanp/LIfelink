using System;

namespace LifeLink.DTOs.Auth
{
    public class UserScreeningProfileDto
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }

        // Pre-fill for screening Section 1: returned to the screening agent only
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public string? BloodGroup { get; set; }
        public DateTime? LastDonationDate { get; set; }
    }
}
