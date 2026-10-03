using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.DTOs.Admin
{
    /// <summary>One-way "Message from Administrator" to exactly one user or one hospital (no replies).</summary>
    public class AdminMessageDto
    {
        public Guid? UserId { get; set; }
        public Guid? HospitalId { get; set; }

        [Required, MaxLength(120)]
        public string Subject { get; set; } = string.Empty;

        [Required, MaxLength(2000)]
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>Reason the admin gives when suspending a blood request or a transfer.</summary>
    public class AdminSuspendItemDto
    {
        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}
