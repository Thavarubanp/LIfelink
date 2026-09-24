using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Complaints;

namespace LifeLink.Services.Complaints
{
    public interface IComplaintService
    {
        Task<ComplaintResponseDto> CreateComplaintAsync(Guid? userId, Guid? hospitalId, CreateComplaintDto dto);
        Task<List<ComplaintResponseDto>> GetComplaintsAsync(string? status = null);
        Task<List<ComplaintResponseDto>> GetMyComplaintsAsync(Guid userId);
        Task<ComplaintResponseDto?> GetComplaintByIdAsync(Guid complaintId);
        Task<ComplaintResponseDto> AdminReplyAsync(Guid complaintId, Guid adminId, ReviewComplaintDto dto);
        Task<ComplaintResponseDto> CreatorReplyAsync(Guid complaintId, Guid userId, ReviewComplaintDto dto);
        Task<ComplaintResponseDto> SolveComplaintAsync(Guid complaintId, Guid userId, string? notes = null);
        Task DeleteComplaintAsync(Guid complaintId, Guid userId);
    }
}
