using System.Collections.Generic;
using System.Linq;
using LifeLink.DTOs.Appeals;
using LifeLink.DTOs.Complaints;

namespace LifeLink.Services.Common
{
    /// <summary>
    /// Complainants and appellants see the same conversation entries as admins, but not which admin acted: admin
    /// emails and IDs are removed from their copy (entries still say whether they came from an admin).
    /// </summary>
    public static class AdminIdentityRedaction
    {
        public static ComplaintResponseDto ForComplainant(ComplaintResponseDto dto)
        {
            dto.AssignedAdminId = null;
            dto.AssignedAdminEmail = null;
            foreach (var log in dto.AuditLogs ?? new List<ComplaintAuditLogDto>())
            {
                log.AdminId = null;
                log.AdminEmail = null;
            }
            foreach (var report in dto.ActivityReports ?? new List<LifeLink.DTOs.HospitalActivity.ActivityReportResponseDto>())
            {
                report.RequestedByAdminId = null;
            }
            return dto;
        }

        public static List<ComplaintResponseDto> ForComplainant(IEnumerable<ComplaintResponseDto> list) =>
            list.Select(ForComplainant).ToList();

        public static AppealResponseDto ForAppellant(AppealResponseDto dto)
        {
            dto.ReviewedByAdminId = null;
            foreach (var message in dto.Messages ?? new List<AppealMessageDto>())
            {
                message.AdminEmail = null;
            }
            return dto;
        }

        public static List<AppealResponseDto> ForAppellant(IEnumerable<AppealResponseDto> list) =>
            list.Select(ForAppellant).ToList();
    }
}
