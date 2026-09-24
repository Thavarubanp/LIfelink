namespace LifeLink.DTOs.BloodRequests
{
    public class BloodRequestAnalyticsDto
    {
        public int UnitsRequired { get; set; }
        public int FulfilledUnits { get; set; }
        public int ReservedUnits { get; set; }
        public int RemainingUnits { get; set; }
        public int AcceptanceCount { get; set; }
        public int MatchedCount { get; set; }
        public int RejectedCount { get; set; }
        public int CompletionPercentage { get; set; }
    }
}
