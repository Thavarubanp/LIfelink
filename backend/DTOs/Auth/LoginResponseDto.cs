using System;

namespace LifeLink.DTOs.Auth
{
    public class LoginResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public CurrentUserDto User { get; set; } = null!;
    }
}
