using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LifeLink.DTOs.Planning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.Planning
{
    public class PlanningAgentService : IPlanningAgentService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PlanningAgentService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public PlanningAgentService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<PlanningAgentService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<PlanResponseDto?> DispatchPlanAsync(PlanRequestDto request)
        {
            var baseUrl = _configuration["PlanningAgent:BaseUrl"] ?? "http://localhost:8004";
            var targetUrl = $"{baseUrl.TrimEnd('/')}/plan";

            try
            {
                _logger.LogInformation("Dispatching event '{EventType}' to LifeLink Planning Agent at {Url}", request.EventType, targetUrl);

                var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(targetUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Planning Agent returned HTTP {StatusCode}: {Error}", response.StatusCode, errorBody);
                    return null;
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                var plan = JsonSerializer.Deserialize<PlanResponseDto>(responseBody, _jsonOptions);

                _logger.LogInformation(
                    "Planning Agent completed workflow '{Workflow}' with status '{Status}' for event '{EventType}'",
                    plan?.Workflow,
                    plan?.WorkflowStatus,
                    request.EventType
                );

                return plan;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reach Planning Agent at {Url}. Event: {EventType}", targetUrl, request.EventType);
                return null;
            }
        }
    }
}
