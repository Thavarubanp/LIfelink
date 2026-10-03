using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LifeLink.DTOs.Acceptances
{
    /// <summary>
    /// What the donor submitted in their latest screening report version (7.3): the questions and their own answers only,
    /// never the AI risk level, flags or summary. CanEdit is true while that version waits for the doctor.
    /// </summary>
    public class ScreeningAnswersDto
    {
        public Guid AcceptanceId { get; set; }
        public int ReportVersion { get; set; }
        public string Status { get; set; } = string.Empty;   // Pending (waiting for the doctor), Approved, Rejected, ...
        public DateTime SubmittedAt { get; set; }
        public bool CanEdit { get; set; }
        public string? EditUnavailableReason { get; set; }
        /// <summary>The 7 questions with their parts (report schema v2); null for reports from the earlier questionnaire.</summary>
        public JsonElement? Questionnaire { get; set; }
        /// <summary>The stored answer of every part (field id -> value), used to prefill the edit form.</summary>
        public JsonElement? Answers { get; set; }
        /// <summary>Readable answers per question/section, in the order asked.</summary>
        public List<ScreeningAnswerSectionDto> Sections { get; set; } = new();
    }

    public class ScreeningAnswerSectionDto
    {
        public int Index { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool Confidential { get; set; }
        public List<ScreeningAnswerItemDto> Items { get; set; } = new();
    }

    public class ScreeningAnswerItemDto
    {
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
    }

    /// <summary>The edit form: field id -> value for all 7 questions, plus CONFIRM_TRUE = "Yes".</summary>
    public class UpdateScreeningAnswersDto
    {
        public JsonElement Answers { get; set; }
    }
}
