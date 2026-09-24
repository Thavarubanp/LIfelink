using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.Admin
{
    public class AdminUserResponseDto
    {
        public Guid UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string AccountStatus { get; set; } = string.Empty;
        public bool IsSuspended { get; set; }
        public DateTime? SuspendedUntil { get; set; }
        public string? SuspensionReason { get; set; }
        public bool IsPermanentlyBlocked { get; set; }
        public List<string> Roles { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }
}
