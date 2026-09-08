using System;

namespace LifeLink.DTOs.Auth
{
    public class RegisterResponseDto
    {
        public Guid UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string AccountStatus { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
