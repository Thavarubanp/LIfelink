using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LifeLink.DTOs.Planning
{
    public class PlanResponseDto
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("eventType")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("workflow")]
        public string Workflow { get; set; } = string.Empty;

        [JsonPropertyName("workflowStatus")]
        public string WorkflowStatus { get; set; } = string.Empty;

        [JsonPropertyName("executionPlan")]
        public ExecutionPlanDto? ExecutionPlan { get; set; }

        [JsonPropertyName("executionTrace")]
        public List<string> ExecutionTrace { get; set; } = new();

        // Alerts composed by the Notification agent; the backend saves them after checking every recipient
        [JsonPropertyName("notifications")]
        public List<AgentNotificationDto> Notifications { get; set; } = new();
    }

    public class AgentNotificationDto
    {
        [JsonPropertyName("recipientType")]
        public string RecipientType { get; set; } = string.Empty; // Donor or Hospital

        [JsonPropertyName("recipientId")]
        public string? RecipientId { get; set; }

        [JsonPropertyName("notificationType")]
        public string? NotificationType { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    public class ExecutionPlanDto
    {
        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("workflowStatus")]
        public string WorkflowStatus { get; set; } = string.Empty;

        [JsonPropertyName("actionItems")]
        public List<PlanActionItemDto> ActionItems { get; set; } = new();

        [JsonPropertyName("results")]
        public Dictionary<string, object> Results { get; set; } = new();
    }

    public class PlanActionItemDto
    {
        [JsonPropertyName("agent")]
        public string Agent { get; set; } = string.Empty;

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("details")]
        public Dictionary<string, object> Details { get; set; } = new();
    }
}
