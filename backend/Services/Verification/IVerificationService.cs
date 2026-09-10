using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Verification;

namespace LifeLink.Services.Verification
{
    public interface IVerificationService
    {
        Task<BloodRequestVerificationResponseDto> ApproveBloodRequestAsync(Guid requestId, ApproveRejectRequestDto dto);
        Task<BloodRequestVerificationResponseDto> RejectBloodRequestAsync(Guid requestId, ApproveRejectRequestDto dto);
        Task<DonorVerificationResponseDto> ApproveDonorVerificationAsync(Guid id, ApproveRejectRequestDto dto);
        Task<DonorVerificationResponseDto> RejectDonorVerificationAsync(Guid id, ApproveRejectRequestDto dto);
        Task<List<BloodRequestVerificationResponseDto>> GetBloodRequestVerificationsAsync();
        Task<List<DonorVerificationResponseDto>> GetDonorVerificationsAsync();
    }
}
