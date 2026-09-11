using System.Threading.Tasks;

namespace LifeLink.Services.BloodRequests
{
    public interface IRequestExpiryService
    {
        Task<int> ProcessExpiredRequestsAsync();
    }
}
