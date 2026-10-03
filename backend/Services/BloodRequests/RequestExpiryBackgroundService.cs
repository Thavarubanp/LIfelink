using System;
using System.Threading;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.Services.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.BloodRequests
{
    /// <summary>
    /// Every 5 minutes: expires overdue blood requests and blood packets. Every InventoryMonitoring:IntervalMinutes
    /// (default 30): runs the scheduled inventory analysis (InventoryAnalysisService: shared lock with manual runs, last
    /// scheduled run read from InventoryAnalysisRuns, so restarts do not cause extra runs).
    /// </summary>
    public class RequestExpiryBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RequestExpiryBackgroundService> _logger;
        private readonly TimeSpan _period = TimeSpan.FromMinutes(5);
        private readonly bool _inventoryCheckEnabled;

        // Sweep lease: this instance's id, and a lease longer than one period so the holder keeps it while running
        private readonly string _instanceId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        private readonly TimeSpan _leaseDuration = TimeSpan.FromMinutes(7);

        public RequestExpiryBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<RequestExpiryBackgroundService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
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
                    await RunRoundAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while running the background sweep.");
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

        /// <summary>
        /// One sweep round. When several backend instances run against the same database, only the instance holding the
        /// sweep lease runs it (the others skip the round), so hospitals never get the same inventory alert twice and the
        /// sweeps do not collide. The holder renews the lease every round; if it stops, another instance takes over once
        /// the lease expires. Each job starts with an empty change tracker, so a job that failed on a conflict never
        /// leaves half-made changes for the next one.
        /// </summary>
        private async Task RunRoundAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await BackgroundJobLeases.TryAcquireAsync(db, BackgroundJobLeases.SweepJob, _instanceId, _leaseDuration))
            {
                _logger.LogInformation("Another backend instance is running the background sweep; skipping this round.");
                return;
            }

            try
            {
                db.ChangeTracker.Clear();
                var expiryService = scope.ServiceProvider.GetRequiredService<IRequestExpiryService>();
                await expiryService.ProcessExpiredRequestsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing expired blood requests in background.");
            }

            try
            {
                db.ChangeTracker.Clear();
                var inventoryService = scope.ServiceProvider.GetRequiredService<IBloodInventoryService>();
                var expired = await inventoryService.ProcessExpiredPacketsAsync();
                if (expired > 0) _logger.LogInformation("Marked {Count} blood packets as expired.", expired);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while expiring blood packets in background.");
            }

            try
            {
                db.ChangeTracker.Clear();
                await IdempotencyKeys.DeleteOlderThanAsync(db, DateTime.UtcNow - IdempotencyKeys.RetentionPeriod);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while removing old idempotency keys.");
            }

            if (_inventoryCheckEnabled)
            {
                try
                {
                    db.ChangeTracker.Clear();
                    var analysis = scope.ServiceProvider.GetRequiredService<InventoryAnalysisService>();
                    var run = await analysis.RunScheduledIfDueAsync();
                    if (run != null) _logger.LogInformation("Scheduled inventory analysis {Status}: {Low} low-stock, {Expiring} expiring-soon alert(s).",
                        run.Status, run.LowStockAlerts, run.ExpiringAlerts);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while running the scheduled inventory check.");
                }
            }
        }
    }
}
