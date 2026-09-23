using System;
using System.Collections.Generic;

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
        public bool CanViewInventory { get; set; }
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
        public DateTime CreatedAt { get; set; }
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
    }
}
