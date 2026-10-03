using System;

namespace LifeLink.DTOs.Doctors
{
    public class DoctorResponseDto
    {
        public Guid DoctorId { get; set; }
        public Guid HospitalId { get; set; }
        public string HospitalName { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string LicenseNumber { get; set; } = string.Empty;
        public string Specialization { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool MustChangePassword { get; set; }
        // Set when the hospital removed the doctor (only the Admin still sees removed doctors)
        public DateTime? DeletedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
