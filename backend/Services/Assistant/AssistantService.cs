using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Assistant;
using LifeLink.Entities;
using LifeLink.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.Assistant
{
    public interface IAssistantService
    {
        Task<AssistantChatResponseDto> ChatAsync(Guid userId, IReadOnlyCollection<string> roles, string? email, AssistantChatRequestDto dto);
    }

    public class AssistantUnavailableException : Exception
    {
        public AssistantUnavailableException(string message) : base(message) { }
    }

    /// <summary>
    /// Relays assistant messages to the Supervisor agent with a role-scoped snapshot of the caller's own data.
    /// For the screening interview the backend first checks that the acceptance belongs to the signed-in donor.
    /// </summary>
    public class AssistantService : IAssistantService
    {
        private const int MaxHistoryTurns = 10;
        private static readonly AcceptanceStatus[] ScreeningStatuses =
            { AcceptanceStatus.Accepted, AcceptanceStatus.ScreeningPending, AcceptanceStatus.ScreeningCompleted };

        private readonly AppDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AssistantService> _logger;
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

        public AssistantService(AppDbContext context, HttpClient httpClient, IConfiguration configuration, ILogger<AssistantService> logger)
        {
            _context = context;
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<AssistantChatResponseDto> ChatAsync(Guid userId, IReadOnlyCollection<string> roles, string? email, AssistantChatRequestDto dto)
        {
            var user = await _context.Users.FindAsync(userId) ?? throw new UnauthorizedAccessException("User not found.");
            var role = AssistantContextBuilder.RoleOf(roles);

            if (dto.AcceptanceId.HasValue)
            {
                await RequireOwnScreeningAsync(userId, role, dto.AcceptanceId.Value);
            }

            var snapshot = await new AssistantContextBuilder(_context).BuildAsync(user, role, email);
            var body = new
            {
                mode = dto.AcceptanceId.HasValue ? "screening" : "assistant",
                message = (dto.Message ?? string.Empty).Trim(),
                history = (dto.History ?? new List<AssistantTurnDto>())
                    .TakeLast(MaxHistoryTurns)
                    .Select(t => new { role = t.Role == "assistant" ? "assistant" : "user", content = (t.Content ?? string.Empty).Length > 2000 ? t.Content![..2000] : t.Content ?? string.Empty }),
                acceptanceId = dto.AcceptanceId?.ToString(),
                user = new { role, firstName = user.FirstName },
                snapshot
            };

            var baseUrl = _configuration["PlanningAgent:BaseUrl"] ?? "http://127.0.0.1:8004";
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat")
            {
                Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json")
            };
            var key = _configuration["InternalService:ApiKey"];
            if (!string.IsNullOrWhiteSpace(key)) request.Headers.Add("X-Internal-Key", key);

            try
            {
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Supervisor chat returned HTTP {Status}", (int)response.StatusCode);
                    throw new AssistantUnavailableException("The LifeLink assistant could not answer right now. Please try again.");
                }
                var result = JsonSerializer.Deserialize<AssistantChatResponseDto>(await response.Content.ReadAsStringAsync(), Json);
                return result ?? throw new AssistantUnavailableException("The LifeLink assistant returned an empty answer.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Supervisor agent unreachable for chat.");
                throw new AssistantUnavailableException("The LifeLink assistant is offline right now. Please try again later.");
            }
            catch (TaskCanceledException)
            {
                throw new AssistantUnavailableException("The LifeLink assistant took too long to answer. Please try again.");
            }
        }

        private async Task RequireOwnScreeningAsync(Guid userId, string role, Guid acceptanceId)
        {
            if (role != "Donor")
            {
                throw new UnauthorizedAccessException("Only donors take part in the screening interview.");
            }

            var acceptance = await _context.Acceptances.FindAsync(acceptanceId);
            if (acceptance == null || acceptance.DonorUserId != userId)
            {
                throw new UnauthorizedAccessException("This screening interview does not belong to you.");
            }

            await DonorEligibility.RequireEligibleDonorAccountAsync(_context, userId);

            if (!ScreeningStatuses.Contains(acceptance.Status))
            {
                throw new InvalidOperationException($"Screening is closed for this donation ({acceptance.Status}).");
            }
        }
    }
}
