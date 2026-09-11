using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.BloodRequests
{
    public class RequestExpiryBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RequestExpiryBackgroundService> _logger;
        private readonly TimeSpan _period = TimeSpan.FromMinutes(5);

        public RequestExpiryBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<RequestExpiryBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RequestExpiryBackgroundService is starting.");

            using var timer = new PeriodicTimer(_period);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var expiryService = scope.ServiceProvider.GetRequiredService<IRequestExpiryService>();
                    await expiryService.ProcessExpiredRequestsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing expired blood requests in background.");
                }

                try
                {
                    if (!await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("RequestExpiryBackgroundService is stopping.");
        }
    }
}
