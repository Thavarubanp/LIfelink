namespace LifeLink.DTOs.Admin
{
    public class AdminDashboardStatsDto
    {
        public int TotalUsers { get; set; }
        public int TotalHospitals { get; set; }
        public int TotalDoctors { get; set; }
        public int ActiveRequests { get; set; }
        public int PendingComplaints { get; set; }
        public int PendingHospitalApprovals { get; set; }
        public int PendingAppeals { get; set; }
        public int ActiveSuspendedUsers { get; set; }
        public int ActiveSuspendedHospitals { get; set; }
    }
}
