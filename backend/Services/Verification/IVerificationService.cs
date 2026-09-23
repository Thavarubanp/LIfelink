using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Verification;

namespace LifeLink.Services.Verification
{
    public interface IVerificationService
    {
        // Hospital actions (hospitalId resolved from the signed-in hospital account)
        Task<BloodRequestVerificationResponseDto> VerifyBloodRequestAsync(Guid requestId, Guid hospitalId, Guid doctorId);
        Task RejectBloodRequestByHospitalAsync(Guid requestId, Guid hospitalId, string? reason);

        // Assigned doctor actions (doctor resolved from the signed-in user)
        Task<BloodRequestVerificationResponseDto> ApproveBloodRequestAsync(Guid requestId, Guid doctorUserId, string? notes);
        Task<BloodRequestVerificationResponseDto> RejectBloodRequestAsync(Guid requestId, Guid doctorUserId, string? reason);

        Task<DonorVerificationResponseDto> ApproveDonorVerificationAsync(Guid id, ApproveRejectRequestDto dto);
        Task<DonorVerificationResponseDto> RejectDonorVerificationAsync(Guid id, ApproveRejectRequestDto dto);
        Task<List<BloodRequestVerificationResponseDto>> GetBloodRequestVerificationsAsync();
        Task<List<DonorVerificationResponseDto>> GetDonorVerificationsAsync();
    }
}
