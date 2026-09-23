using System;

namespace LifeLink.Entities
{
    public class Doctor
    {
        public Guid DoctorId { get; set; } = Guid.NewGuid();
        public Guid HospitalId { get; set; }
        public Guid? UserId { get; set; }

        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string LicenseNumber { get; set; } = string.Empty;
        public string Specialization { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// When true, the doctor must change their password before accessing the dashboard.
        /// Set to true on creation (by hospital) and cleared to false after first successful password change.
        /// </summary>
        public bool MustChangePassword { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Hospital Hospital { get; set; } = null!;
        public User? User { get; set; }
    }
}

