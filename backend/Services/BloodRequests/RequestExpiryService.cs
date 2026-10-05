using System;
using System.Threading.Tasks;
using LifeLink.Data;
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
            // Intentionally retained as a compatibility no-op for existing DI/tests. Blood requests are closed only by
            // explicit workflow actions; elapsed time never changes their status. Packet expiry is a separate service.
            await Task.CompletedTask;
            _logger.LogDebug("Blood request expiry sweep skipped: blood requests do not expire automatically.");
            return 0;
        }
    }
}
