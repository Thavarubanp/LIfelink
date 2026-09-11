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

            var expiredRequests = await _context.BloodRequests
                .Where(r => r.ExpiryDate <= now &&
                            r.Status != BloodRequestStatus.Completed &&
                            r.Status != BloodRequestStatus.Cancelled &&
                            r.Status != BloodRequestStatus.Rejected)
                .ToListAsync();

            if (!expiredRequests.Any())
            {
                return 0;
            }

            foreach (var req in expiredRequests)
            {
                req.Status = BloodRequestStatus.Rejected;
                req.UpdatedAt = now;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Processed and expired {Count} blood requests.", expiredRequests.Count);

            return expiredRequests.Count;
        }
    }
}
