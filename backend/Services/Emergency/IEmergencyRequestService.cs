using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Emergency;

namespace LifeLink.Services.Emergency
{
    public interface IEmergencyRequestService
    {
        Task<EmergencyRequestResponseDto> CreateEmergencyRequestAsync(EmergencyRequestCreateDto dto);
        Task<EmergencyRequestResponseDto?> GetEmergencyRequestAsync(Guid id);
        Task<IEnumerable<EmergencyRequestResponseDto>> GetAllEmergencyRequestsAsync();
        Task<EmergencyRequestResponseDto> ApproveEmergencyRequestAsync(Guid id);
        Task<EmergencyRequestResponseDto> RejectEmergencyRequestAsync(Guid id);
        Task<EmergencyRequestResponseDto> CompleteEmergencyRequestAsync(Guid id);
        Task<IEnumerable<EmergencyRequestResponseDto>> GetCriticalEmergencyRequestsAsync();
    }
}
