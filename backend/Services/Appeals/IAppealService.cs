using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Appeals;

namespace LifeLink.Services.Appeals
{
    public interface IAppealService
    {
        Task<AppealResponseDto> SubmitAppealAsync(CreateAppealDto dto, Guid? currentUserId = null);
        Task<List<AppealResponseDto>> GetAppealsAsync(string? status = null);
        Task<AppealResponseDto?> GetAppealByIdAsync(Guid appealId);
        Task<List<AppealResponseDto>> GetMyAppealsAsync(Guid userId);
        Task<AppealResponseDto> ApproveAppealAsync(Guid appealId, Guid adminId, ReviewAppealDto dto);
        Task<AppealResponseDto> RejectAppealAsync(Guid appealId, Guid adminId, ReviewAppealDto dto);
        Task<AppealResponseDto> PermanentlyBlockAsync(Guid appealId, Guid adminId, ReviewAppealDto dto);
        Task<AppealResponseDto> CloseAppealAsync(Guid appealId, Guid adminId, ReviewAppealDto dto);
        Task<AppealResponseDto> AppellantReplyAsync(Guid appealId, Guid userId, LifeLink.DTOs.Complaints.ReviewComplaintDto dto);
        Task<AppealResponseDto> AdminReplyAsync(Guid appealId, Guid adminId, LifeLink.DTOs.Complaints.ReviewComplaintDto dto);
    }
}
