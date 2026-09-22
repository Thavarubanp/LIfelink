using System;
using System.Collections.Generic;

namespace LifeLink.Entities
{
    public class User
    {
        public Guid UserId { get; set; } = Guid.NewGuid();

        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;

        public AccountStatus AccountStatus { get; set; } = AccountStatus.Active;

        // Student 4 Additions: Suspension Policy
        public bool IsSuspended { get; set; } = false;
        public DateTime? SuspendedUntil { get; set; }
        public string? SuspensionReason { get; set; }
        public bool IsPermanentlyBlocked { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
        public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    }
}
