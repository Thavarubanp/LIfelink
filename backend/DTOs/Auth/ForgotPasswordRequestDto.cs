using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Auth
{
    public class ForgotPasswordRequestDto
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        public string Email { get; set; } = string.Empty;
    }
}
