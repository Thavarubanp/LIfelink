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
        Task<IEnumerable<AcceptanceResponseDto>> GetMyAcceptancesAsync(Guid donorUserId);
        Task<AcceptanceResponseDto?> GetAcceptanceByIdAsync(Guid acceptanceId);
        Task<List<RequestAcceptanceDetailDto>> GetRequestAcceptancesAsync(Guid bloodRequestId);
        Task<FinalizeDonorSelectionResponseDto> FinalizeDonorSelectionAsync(Guid bloodRequestId, List<Guid> selectedAcceptanceIds, Guid doctorUserId);
        Task<AcceptanceResponseDto> UpdateScreeningStatusAsync(Guid acceptanceId, AcceptanceStatus newStatus);
    }
}
