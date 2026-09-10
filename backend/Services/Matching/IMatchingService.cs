using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Matching;

namespace LifeLink.Services.Matching
{
    public interface IMatchingService
    {
        Task<DonorPatientMatchResponseDto> CreateMatchAsync(CreateMatchDto dto);
        Task<List<DonorPatientMatchResponseDto>> GetMatchesAsync();
        Task<DonorPatientMatchResponseDto?> GetMatchByIdAsync(Guid matchId);
    }
}
