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
