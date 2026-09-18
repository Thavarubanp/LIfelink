using System.Threading.Tasks;
using LifeLink.DTOs.Planning;

namespace LifeLink.Services.Planning
{
    public interface IPlanningAgentService
    {
        Task<PlanResponseDto?> DispatchPlanAsync(PlanRequestDto request);
    }
}
