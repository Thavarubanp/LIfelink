using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.Acceptances
{
    /// <summary>The Request Management agent could not be reached (or failed): nothing was changed.</summary>
    public class ScreeningAgentUnavailableException : Exception
    {
        public ScreeningAgentUnavailableException(string message) : base(message) { }
    }

    /// <summary>
    /// The screening edit form (7.2) talks to the Request Management agent directly: first a check of the answers (nothing
    /// saved), then, after the backend has superseded the old report version, the submission (the agent re-checks the
    /// answers and submits the new version in the background; the chat interview does not run again).
    /// </summary>
    public interface IScreeningAgentClient
    {
        /// <summary>The problems to fix (empty = valid). Throws ScreeningAgentUnavailableException when unreachable.</summary>
        Task<List<string>> ValidateAnswersAsync(Guid acceptanceId, JsonElement answers);

        /// <summary>True when the agent accepted the answers for a new report version.</summary>
        Task<bool> SubmitAnswersAsync(Guid acceptanceId, JsonElement answers);
    }

    public class ScreeningAgentClient : IScreeningAgentClient
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ScreeningAgentClient> _logger;

        public ScreeningAgentClient(HttpClient http, IConfiguration configuration, ILogger<ScreeningAgentClient> logger)
        {
            _http = http;
            _configuration = configuration;
            _logger = logger;
        }

        private string BaseUrl => (_configuration["ScreeningAgent:BaseUrl"] ?? "http://127.0.0.1:8001").TrimEnd('/');

        private HttpRequestMessage Build(HttpMethod method, string path, JsonElement answers)
        {
            var request = new HttpRequestMessage(method, $"{BaseUrl}{path}")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { answers }), Encoding.UTF8, "application/json")
            };
            var key = _configuration["InternalService:ApiKey"];
            if (!string.IsNullOrWhiteSpace(key)) request.Headers.Add("X-Internal-Key", key);
            return request;
        }

        public async Task<List<string>> ValidateAnswersAsync(Guid acceptanceId, JsonElement answers)
        {
            try
            {
                using var request = Build(HttpMethod.Post, $"/api/agent/screening/answers/{acceptanceId}/validate", answers);
                using var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    throw new ScreeningAgentUnavailableException("The screening assistant could not check your answers right now. Please try again shortly.");
                }
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
                    ? errors.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(e => e.Length > 0).ToList()
                    : new List<string>();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(ex, "Screening agent unreachable while validating answers for {AcceptanceId}", acceptanceId);
                throw new ScreeningAgentUnavailableException("The screening assistant is not available right now. Your answers were not changed; please try again shortly.");
            }
        }

        public async Task<bool> SubmitAnswersAsync(Guid acceptanceId, JsonElement answers)
        {
            try
            {
                using var request = Build(HttpMethod.Put, $"/api/agent/screening/answers/{acceptanceId}", answers);
                using var response = await _http.SendAsync(request);
                return response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Screening agent unreachable while submitting edited answers for {AcceptanceId}", acceptanceId);
                return false;
            }
        }
    }
}
