using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Profiles
{
    public class HospitalProfileDto
    {
        public Guid HospitalId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string LicenseNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
        public string? City { get; set; }
        public string? ContactPersonName { get; set; }
        public string? ContactPersonPhone { get; set; }
        public string? ContactPersonEmail { get; set; }
        public string? RegistrationNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public int DoctorCount { get; set; }
        public int PacketShelfLifeDays { get; set; }
        public int ExpiryAlertDays { get; set; }
        public bool CanViewInventory { get; set; }
        public bool CanEdit { get; set; }
        public List<HospitalInventoryItemDto>? Inventory { get; set; }
    }

    public class HospitalInventoryItemDto
    {
        public Guid InventoryId { get; set; }
        public string BloodGroup { get; set; } = string.Empty;
        public int UnitsAvailable { get; set; }
        public int MinimumThreshold { get; set; }
        public int MaximumCapacity { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class UserProfileDto
    {
        public Guid UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
        public string AccountStatus { get; set; } = string.Empty;
        public string DisplayStatus { get; set; } = "Active"; // "Active", "Suspended" or "Permanently Blocked"
        // Donor eligibility details: only returned to the owner and the Admin
        public string? BloodGroup { get; set; }
        public bool BloodGroupConfirmed { get; set; }
        public DateTime? LastDonationDate { get; set; }
        public DateTime? NextEligibleDonationDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool CanEdit { get; set; }
    }

    public class DoctorProfileDto
    {
        public Guid DoctorId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string LicenseNumber { get; set; } = string.Empty; // SLMC Registration Number
        public string Specialization { get; set; } = string.Empty;
        public Guid HospitalId { get; set; }
        public string HospitalName { get; set; } = string.Empty;
        public string HospitalAddress { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool CanEdit { get; set; }
    }

    // Own-profile edit DTOs. Email (and a hospital's license/registration numbers) are intentionally not editable.

    public class UpdateUserProfileDto
    {
        [Required(ErrorMessage = "First name is required.")]
        [StringLength(100, ErrorMessage = "First name cannot exceed 100 characters.")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required.")]
        [StringLength(100, ErrorMessage = "Last name cannot exceed 100 characters.")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone number is required.")]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "Phone number must be exactly 10 digits.")]
        public string PhoneNumber { get; set; } = string.Empty;

        [StringLength(20, ErrorMessage = "Gender cannot exceed 20 characters.")]
        public string? Gender { get; set; }

        [StringLength(500, ErrorMessage = "Address cannot exceed 500 characters.")]
        public string? Address { get; set; }

        // Optional; blood group can change only until a recorded donation confirms it
        public string? BloodGroup { get; set; }
        public DateTime? LastDonationDate { get; set; }
    }

    public class UpdateDoctorProfileDto
    {
        [Required(ErrorMessage = "First name is required.")]
        [StringLength(100, ErrorMessage = "First name cannot exceed 100 characters.")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required.")]
        [StringLength(100, ErrorMessage = "Last name cannot exceed 100 characters.")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone number is required.")]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "Phone number must be exactly 10 digits.")]
        public string PhoneNumber { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "Specialization cannot exceed 100 characters.")]
        public string? Specialization { get; set; }

        [Required(ErrorMessage = "SLMC number is required.")]
        [StringLength(100, ErrorMessage = "SLMC number cannot exceed 100 characters.")]
        public string LicenseNumber { get; set; } = string.Empty; // SLMC Registration Number
    }

    public class UpdateHospitalProfileDto
    {
        [Required(ErrorMessage = "Hospital name is required.")]
        [StringLength(200, ErrorMessage = "Hospital name cannot exceed 200 characters.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Address is required.")]
        [StringLength(500, ErrorMessage = "Address cannot exceed 500 characters.")]
        public string Address { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "City cannot exceed 100 characters.")]
        public string? City { get; set; }

        [Required(ErrorMessage = "Contact number is required.")]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "Contact number must be exactly 10 digits.")]
        public string ContactNumber { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "Contact person name cannot exceed 200 characters.")]
        public string? ContactPersonName { get; set; }

        [RegularExpression(@"^\d{10}$", ErrorMessage = "Contact person phone must be exactly 10 digits.")]
        public string? ContactPersonPhone { get; set; }

        [EmailAddress(ErrorMessage = "Invalid contact person email address.")]
        [StringLength(200, ErrorMessage = "Contact person email cannot exceed 200 characters.")]
        public string? ContactPersonEmail { get; set; }

        // Blood packet settings (optional): shelf life of newly collected packets and the expiry alert window
        [Range(21, 35, ErrorMessage = "Packet shelf life must be between 21 and 35 days.")]
        public int? PacketShelfLifeDays { get; set; }

        [Range(1, 20, ErrorMessage = "Expiry alert window must be between 1 and 20 days.")]
        public int? ExpiryAlertDays { get; set; }
    }

    /// <summary>Which profile page belongs to the caller: Type is "user", "doctor" or "hospital".</summary>
    public class MyProfileDto
    {
        public string Type { get; set; } = string.Empty;
        public Guid Id { get; set; }
    }
}
