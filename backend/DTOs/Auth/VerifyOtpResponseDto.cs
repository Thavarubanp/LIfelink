namespace LifeLink.DTOs.Auth
{
    public class VerifyOtpResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ResetSessionToken { get; set; } = string.Empty;
    }
}
