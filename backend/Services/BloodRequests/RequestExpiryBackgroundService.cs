using System;
using System.Threading;
using System.Threading.Tasks;
using LifeLink.Services.Inventory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.BloodRequests
{
    /// <summary>
    /// Every 5 minutes: expires overdue blood requests and blood packets. Every InventoryMonitoring:IntervalMinutes
    /// (default 30): runs the threshold/expiry inventory check through the Supervisor.
    /// </summary>
    public class RequestExpiryBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RequestExpiryBackgroundService> _logger;
        private readonly TimeSpan _period = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _inventoryCheckPeriod;
        private readonly bool _inventoryCheckEnabled;
        private DateTime _lastInventoryCheck = DateTime.MinValue;

        public RequestExpiryBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<RequestExpiryBackgroundService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _inventoryCheckPeriod = TimeSpan.FromMinutes(Math.Max(5, configuration.GetValue("InventoryMonitoring:IntervalMinutes", 30)));
            _inventoryCheckEnabled = configuration.GetValue("InventoryMonitoring:Enabled", true);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RequestExpiryBackgroundService is starting.");

            try
            {
                await Task.Delay(5000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

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
                    using var scope = _scopeFactory.CreateScope();
                    var inventoryService = scope.ServiceProvider.GetRequiredService<IBloodInventoryService>();
                    var expired = await inventoryService.ProcessExpiredPacketsAsync();
                    if (expired > 0) _logger.LogInformation("Marked {Count} blood packets as expired.", expired);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while expiring blood packets in background.");
                }

                if (_inventoryCheckEnabled && DateTime.UtcNow - _lastInventoryCheck >= _inventoryCheckPeriod)
                {
                    _lastInventoryCheck = DateTime.UtcNow;
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var monitor = scope.ServiceProvider.GetRequiredService<InventoryMonitor>();
                        var alerts = await monitor.RunInventoryCheckAsync();
                        if (alerts > 0) _logger.LogInformation("Inventory check sent {Count} hospital alerts.", alerts);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred while running the scheduled inventory check.");
                    }
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
