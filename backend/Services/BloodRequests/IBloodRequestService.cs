using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.BloodRequests;

namespace LifeLink.Services.BloodRequests
{
    public interface IBloodRequestService
    {
        Task<BloodRequestResponseDto> CreateRequestAsync(Guid patientUserId, CreateBloodRequestDto dto);
        Task<IEnumerable<BloodRequestResponseDto>> GetMyRequestsAsync(Guid patientUserId);
        Task<IEnumerable<BloodRequestResponseDto>> GetHospitalRequestsAsync(Guid hospitalId);
        Task<IEnumerable<BloodRequestResponseDto>> GetAssignedRequestsAsync(Guid doctorUserId);
        Task DeleteRejectedRequestAsync(Guid requestId, Guid creatorUserId);
        Task<BloodRequestResponseDto?> GetRequestByIdAsync(Guid requestId);
        Task<IEnumerable<BloodRequestResponseDto>> GetPublicRequestsAsync(string? bloodGroup = null, int? expiringWithinHours = null);
        Task<IEnumerable<BloodRequestResponseDto>> GetPendingRequestsAsync(Guid? hospitalId = null);
        Task<BloodRequestResponseDto> CancelRequestAsync(Guid requestId, Guid patientUserId);
        Task<BloodRequestAnalyticsDto> GetRequestAnalyticsAsync(Guid requestId);
        Task<IEnumerable<RequestFulfillmentHistoryResponseDto>> GetRequestFulfillmentHistoryAsync(Guid requestId);
    }
}
