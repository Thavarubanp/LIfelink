namespace LifeLink.DTOs.Acceptances
{
    public class ScreeningReportNotificationDto
    {
        public string ReportId { get; set; } = string.Empty;
        public string AcceptanceId { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
