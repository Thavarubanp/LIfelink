using System;

namespace LifeLink.Entities
{
    /// <summary>
    /// One inventory analysis run (scheduled every InventoryMonitoring:IntervalMinutes, or started by hospital staff),
    /// shown to every hospital as "Last analysis". The BackgroundJobLeases row "InventoryAnalysis" is the only lock;
    /// this table only records what happened. Rows are never deleted. The scheduler reads its last scheduled run from
    /// here (Q21), so the schedule holds across restarts.
    /// </summary>
    public class InventoryAnalysisRun
    {
        public Guid RunId { get; set; } = Guid.NewGuid();
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? FinishedAt { get; set; }
        public string Trigger { get; set; } = InventoryAnalysisTriggers.Scheduled;   // Scheduled | Manual
        public Guid? TriggeredByHospitalId { get; set; }
        public Guid? TriggeredByUserId { get; set; }
        public string Status { get; set; } = InventoryAnalysisStatuses.Running;      // Running | Completed | CompletedRuleBased | Failed
        public int LowStockAlerts { get; set; }
        public int ExpiringAlerts { get; set; }
        public int SkippedDuplicates { get; set; }
    }

    public static class InventoryAnalysisTriggers
    {
        public const string Scheduled = "Scheduled";
        public const string Manual = "Manual";
    }

    public static class InventoryAnalysisStatuses
    {
        public const string Running = "Running";
        public const string Completed = "Completed";
        public const string CompletedRuleBased = "CompletedRuleBased"; // the Supervisor was unreachable: rule-based alerts
        public const string Failed = "Failed";
    }
}
