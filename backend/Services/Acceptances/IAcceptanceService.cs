using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Acceptances;
using LifeLink.Entities;

namespace LifeLink.Services.Acceptances
{
    public interface IAcceptanceService
    {
        Task<AcceptanceResponseDto> AcceptRequestAsync(Guid donorUserId, CreateAcceptanceDto dto);
        Task<AcceptanceResponseDto> CancelAcceptanceAsync(Guid acceptanceId, Guid donorUserId);
        Task<AcceptanceResponseDto> ReleaseReservationAsync(Guid acceptanceId, Guid actorUserId, Guid? actingHospitalId, string? reason);
        Task<AcceptanceResponseDto> ReopenScreeningAsync(Guid acceptanceId, Guid donorUserId);
        Task<IEnumerable<AcceptanceResponseDto>> GetMyAcceptancesAsync(Guid donorUserId);
        Task<AcceptanceResponseDto?> GetAcceptanceByIdAsync(Guid acceptanceId);
        Task<List<RequestAcceptanceDetailDto>> GetRequestAcceptancesAsync(Guid bloodRequestId);
        Task<FinalizeDonorSelectionResponseDto> FinalizeDonorSelectionAsync(Guid bloodRequestId, List<Guid> selectedAcceptanceIds, Guid actorUserId, Guid? actingHospitalId = null, Dictionary<Guid, string>? testedBloodGroups = null);
        Task<AcceptanceResponseDto> UpdateScreeningStatusAsync(Guid acceptanceId, AcceptanceStatus newStatus);
        Task<DonorVerification> SubmitScreeningReportAsync(ScreeningReportNotificationDto dto);
    }
}
