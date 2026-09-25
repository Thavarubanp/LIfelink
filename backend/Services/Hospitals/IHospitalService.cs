using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Hospitals;

namespace LifeLink.Services.Hospitals
{
    public interface IHospitalService
    {
        Task<HospitalResponseDto> CreateHospitalAsync(CreateHospitalDto dto);
        Task<List<HospitalSummaryDto>> GetHospitalsAsync(bool? isVerified = null);
        Task<HospitalResponseDto?> GetHospitalByIdAsync(Guid hospitalId, bool includeAdminIdentity = false);
        Task<Guid?> GetHospitalIdByEmailAsync(string? email);
        Task<HospitalResponseDto?> VerifyHospitalAsync(Guid hospitalId, bool isVerified);
        Task<HospitalResponseDto> ReplyToRegistrationAsync(Guid hospitalId, HospitalRegistrationReplyDto dto);
    }
}
