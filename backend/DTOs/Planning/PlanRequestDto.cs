using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LifeLink.DTOs.Planning
{
    public class PlanRequestDto
    {
        [JsonPropertyName("eventType")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("requestId")]
        public string? RequestId { get; set; }

        [JsonPropertyName("bloodGroup")]
        public string? BloodGroup { get; set; }

        [JsonPropertyName("urgency")]
        public string? Urgency { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("unitsRequired")]
        public int? UnitsRequired { get; set; }

        [JsonPropertyName("donorId")]
        public string? DonorId { get; set; }

        [JsonPropertyName("hospitalId")]
        public string? HospitalId { get; set; }

        [JsonPropertyName("payload")]
        public Dictionary<string, object> Payload { get; set; } = new();
    }
}
