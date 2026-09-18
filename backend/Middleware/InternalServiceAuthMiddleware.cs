using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LifeLink.Middleware
{
    public class InternalServiceAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _configuredKey;
        private readonly ILogger<InternalServiceAuthMiddleware> _logger;

        public const string HeaderName = "X-Internal-Key";
        public const string RoleName = "InternalAgent";

        public InternalServiceAuthMiddleware(
            RequestDelegate next,
            IConfiguration configuration,
            ILogger<InternalServiceAuthMiddleware> logger)
        {
            _next = next;
            _configuredKey = configuration["InternalService:ApiKey"]
                ?? Environment.GetEnvironmentVariable("INTERNAL_SERVICE_API_KEY")
                ?? "LifeLink-Internal-Agent-Key-2026";
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var extractedKey))
            {
                if (!string.IsNullOrWhiteSpace(_configuredKey) && string.Equals(extractedKey.ToString(), _configuredKey, StringComparison.Ordinal))
                {
                    var claims = new[]
                    {
                        new Claim(ClaimTypes.Name, "InternalAgentService"),
                        new Claim(ClaimTypes.Role, RoleName),
                        new Claim("scope", "internal_agent_read_write")
                    };

                    var identity = new ClaimsIdentity(claims, "InternalKeyAuth");
                    context.User = new ClaimsPrincipal(identity);

                    _logger.LogDebug("Authenticated internal agent microservice via {HeaderName}", HeaderName);
                }
                else
                {
                    _logger.LogWarning("Invalid internal service key supplied in {HeaderName}", HeaderName);
                }
            }

            await _next(context);
        }
    }
}
