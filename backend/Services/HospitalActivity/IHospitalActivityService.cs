using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.HospitalActivity;

namespace LifeLink.Services.HospitalActivity
{
    public interface IHospitalActivityService
    {
        Task<ActivityReportResponseDto> SubmitActivityReportAsync(SubmitActivityReportDto dto);
        Task<List<ActivityReportResponseDto>> GetActivityReportsAsync(Guid? complaintId = null, Guid? hospitalId = null);
        Task<ActivityReportResponseDto?> GetActivityReportByIdAsync(Guid reportId);
    }
}
