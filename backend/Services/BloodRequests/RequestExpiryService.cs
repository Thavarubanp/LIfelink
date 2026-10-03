using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.BloodRequests
{
    public class RequestExpiryService : IRequestExpiryService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<RequestExpiryService> _logger;

        public RequestExpiryService(AppDbContext context, ILogger<RequestExpiryService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<int> ProcessExpiredRequestsAsync()
        {
            var now = DateTime.UtcNow;

            var expiredIds = await _context.BloodRequests
                .Where(r => r.ExpiryDate <= now &&
                            r.AdminSuspendedAt == null && // suspended requests are skipped (Q7)
                            r.Status != BloodRequestStatus.Completed &&
                            r.Status != BloodRequestStatus.Cancelled &&
                            r.Status != BloodRequestStatus.Rejected &&
                            r.Status != BloodRequestStatus.Deleted)
                .Select(r => r.BloodRequestId)
                .ToListAsync();

            if (!expiredIds.Any())
            {
                return 0;
            }

            // Each request is expired in its own save: someone acting on one request at the same moment (409) only
            // postpones that request to the next run instead of failing the whole batch
            var expired = 0;
            foreach (var id in expiredIds)
            {
                try
                {
                    var req = await _context.BloodRequests.FindAsync(id);
                    if (req == null || !BloodRequestService.IsExpiredAndOpen(req, now)) continue;

                    // Also releases donors still in progress (reserved slots and their one-active-donation lock)
                    await BloodRequestService.ExpireAsync(_context, req, now);
                    await _context.SaveChangesAsync();
                    expired++;
                }
                catch (DbUpdateConcurrencyException)
                {
                    _context.ChangeTracker.Clear();
                    _logger.LogInformation("Blood request {RequestId} changed while expiring; it will be retried.", id);
                }
            }

            _logger.LogInformation("Processed and expired {Count} blood requests.", expired);

            return expired;
        }
    }
}
