using System;

namespace LifeLink.DTOs.Auth
{
    public class UserScreeningProfileDto
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
    }
}
