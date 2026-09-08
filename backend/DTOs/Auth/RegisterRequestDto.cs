using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Auth
{
    public class RegisterRequestDto
    {
        [Required(ErrorMessage = "First name is required.")]
        [StringLength(100, ErrorMessage = "First name cannot exceed 100 characters.")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required.")]
        [StringLength(100, ErrorMessage = "Last name cannot exceed 100 characters.")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address format.")]
        [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$", 
            ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character.")]
        public string Password { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Invalid phone number format.")]
        [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
        public string? PhoneNumber { get; set; }

        public DateTime? DateOfBirth { get; set; }

        [StringLength(20, ErrorMessage = "Gender cannot exceed 20 characters.")]
        public string? Gender { get; set; }

        [StringLength(500, ErrorMessage = "Address cannot exceed 500 characters.")]
        public string? Address { get; set; }
    }
}
