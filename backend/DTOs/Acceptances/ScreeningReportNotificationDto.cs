namespace LifeLink.DTOs.Acceptances
{
    /// <summary>Screening report submitted by the Request Management agent (one immutable version per submission).</summary>
    public class ScreeningReportNotificationDto
    {
        public string ReportId { get; set; } = string.Empty;
        public string AcceptanceId { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string? ReportJson { get; set; }
    }
}
