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
        Task<EmergencyRequestResponseDto> ApproveEmergencyRequestAsync(Guid id, Guid? actingHospitalId = null);
        Task<EmergencyRequestResponseDto> RejectEmergencyRequestAsync(Guid id, Guid? actingHospitalId = null);
        Task<EmergencyRequestResponseDto> CompleteEmergencyRequestAsync(Guid id, Guid? actingHospitalId = null, Guid? performedByUserId = null);
        Task<IEnumerable<EmergencyRequestResponseDto>> GetCriticalEmergencyRequestsAsync();
    }
}
