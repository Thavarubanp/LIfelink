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

        // Donor screening decisions (doctor resolved from the signed-in user; AI agents never decide)
        Task<DonorVerificationResponseDto> ApproveDonorVerificationAsync(Guid id, Guid doctorUserId, string? notes);
        Task<DonorVerificationResponseDto> RejectDonorVerificationAsync(Guid id, Guid doctorUserId, string? reason);
        Task<List<BloodRequestVerificationResponseDto>> GetBloodRequestVerificationsAsync();
        Task<List<DonorVerificationResponseDto>> GetDonorVerificationsAsync(Guid? doctorUserId);
    }
}
