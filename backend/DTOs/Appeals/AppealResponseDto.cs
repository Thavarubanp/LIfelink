using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.Appeals
{
    public class AppealResponseDto
    {
        public Guid AppealId { get; set; }
        public Guid? UserId { get; set; }
        public string? UserEmail { get; set; }
        public Guid? HospitalId { get; set; }
        public string? HospitalName { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public Guid? ReviewedByAdminId { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? AdminResponse { get; set; }

        // Conversation thread (appellant <-> admin); replies alternate
        public List<AppealMessageDto> Messages { get; set; } = new();
        public bool IsClosed { get; set; }            // APPROVED or CLOSED: read-only
        public bool AwaitingAdminReply { get; set; }  // appellant spoke last
        public bool CanAppellantReply { get; set; }   // admin spoke last and the thread is open
    }

    public class AppealMessageDto
    {
        public Guid MessageId { get; set; }
        public bool FromAdmin { get; set; }
        public string? AdminEmail { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string? AttachmentName { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
