using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace LifeLink.DTOs.Assistant
{
    public class AssistantChatRequestDto
    {
        [StringLength(2000, ErrorMessage = "Messages are limited to 2000 characters.")]
        public string Message { get; set; } = string.Empty;

        /// <summary>Recent conversation kept by the browser (the backend stores no chat history).</summary>
        public List<AssistantTurnDto> History { get; set; } = new();

        /// <summary>Set for the donor screening interview of this acceptance.</summary>
        public Guid? AcceptanceId { get; set; }
    }

    public class AssistantTurnDto
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;
    }

    public class AssistantChatResponseDto
    {
        public string Reply { get; set; } = string.Empty;
        public List<AssistantSegmentDto> Segments { get; set; } = new();
        public List<AssistantActionDto> Actions { get; set; } = new();
        public JsonElement? Screening { get; set; }
    }

    /// <summary>Type: medical (guidance with sources), platform (LifeLink guide), account (own data), screening.</summary>
    public class AssistantSegmentDto
    {
        public string Type { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public List<AssistantSourceDto> Sources { get; set; } = new();
    }

    public class AssistantSourceDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Publisher { get; set; }
        public string? Url { get; set; }
    }

    public class AssistantActionDto
    {
        public string Label { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
    }
}
