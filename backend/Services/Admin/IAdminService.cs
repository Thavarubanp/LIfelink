using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Admin;

namespace LifeLink.Services.Admin
{
    public interface IAdminService
    {
        Task<AdminDashboardStatsDto> GetDashboardStatsAsync();
        Task<List<AdminUserResponseDto>> GetUsersAsync();
        Task<List<AdminHospitalResponseDto>> GetAllHospitalsAsync();
        Task<List<AdminHospitalResponseDto>> GetPendingHospitalsAsync();
        Task<AdminHospitalResponseDto> ApproveHospitalAsync(Guid hospitalId, Guid adminId);
        Task<AdminHospitalResponseDto> RejectHospitalAsync(Guid hospitalId, Guid adminId, RejectHospitalDto dto);
        Task<AdminUserResponseDto> SuspendUserAsync(Guid userId, SuspendUserDto dto, Guid? actingAdminId = null);
        Task<AdminUserResponseDto> ReinstateUserAsync(Guid userId, Guid? actingAdminId = null);
        Task<AdminUserResponseDto> BlockUserAsync(Guid userId, Guid actingAdminId);
        Task PromoteToAdminAsync(Guid userId, Guid actingAdminId);
        Task<AdminHospitalResponseDto> SuspendHospitalAsync(Guid hospitalId, SuspendHospitalDto dto);
        Task<AdminHospitalResponseDto> ReinstateHospitalAsync(Guid hospitalId);
    }
}
