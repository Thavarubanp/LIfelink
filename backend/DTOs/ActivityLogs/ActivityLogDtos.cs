using System;
using System.Collections.Generic;

namespace LifeLink.DTOs.ActivityLogs
{
    public class ActivityLogEntryDto
    {
        public Guid Id { get; set; }
        public DateTime OccurredAt { get; set; }
        public string ActorRole { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public Guid? EntityId { get; set; }
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>One page of an activity log, newest first.</summary>
    public class ActivityLogPageDto
    {
        public List<ActivityLogEntryDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        /// <summary>Activity is recorded from this date on (no older actions).</summary>
        public DateTime RecordedFrom { get; set; }
        /// <summary>The action types the filter offers.</summary>
        public IReadOnlyList<string> Types { get; set; } = Array.Empty<string>();
    }

    /// <summary>Query string of every activity log view: ?page=1&amp;pageSize=20&amp;type=BloodRequest&amp;from=2026-10-01&amp;to=2026-10-03</summary>
    public class ActivityLogQueryDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        /// <summary>Action type (ActivityLog.EntityType); empty = all.</summary>
        public string? Type { get; set; }
        /// <summary>First day, inclusive (Sri Lanka calendar date).</summary>
        public DateTime? From { get; set; }
        /// <summary>Last day, inclusive (Sri Lanka calendar date).</summary>
        public DateTime? To { get; set; }
    }
}
