using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.Auth
{
    public class CurrentUserDto
    {
        public Guid UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new List<string>();
        public string AccountStatus { get; set; } = string.Empty;
        public bool IsSuspended { get; set; } = false;
        /// <summary>
        /// True only for Doctor accounts that have not yet changed their hospital-assigned password.
        /// Frontend uses this to force redirect to /doctor/change-password.
        /// </summary>
        public bool MustChangePassword { get; set; } = false;
    }
}

