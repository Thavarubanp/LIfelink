using System;

namespace LifeLink.DTOs.Admin
{
    /// <summary>
    /// Admin sidebar/dashboard badges. "New" counts are items created since the admin last opened that Activity log tab
    /// (SeenAt is null when never opened); "pending" counts are items waiting for the admin and clear when handled.
    /// </summary>
    public class AdminAttentionDto
    {
        public int NewBloodRequests { get; set; }
        public int NewTransfers { get; set; }
        public DateTime? BloodRequestsSeenAt { get; set; }
        public DateTime? TransfersSeenAt { get; set; }
        public int PendingRegistrations { get; set; }
        public int PendingAppeals { get; set; }
        public int PendingComplaints { get; set; }
    }
}
