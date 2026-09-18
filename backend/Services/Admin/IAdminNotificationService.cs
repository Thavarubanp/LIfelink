using System;
using System.Threading.Tasks;
using LifeLink.Entities;

namespace LifeLink.Services.Admin
{
    public interface IAdminNotificationService
    {
        Task NotifyHospitalApprovedAsync(Hospital hospital);
        Task NotifyHospitalRejectedAsync(Hospital hospital, string reason);
        Task NotifyUserSuspendedAsync(User user, string reason, DateTime? until);
        Task NotifyHospitalSuspendedAsync(Hospital hospital, string reason, DateTime? until);
        Task NotifyUserReinstatedAsync(User user);
        Task NotifyHospitalReinstatedAsync(Hospital hospital);
        Task NotifyComplaintResolvedAsync(Complaint complaint);
        Task NotifyActivityReportRequestedAsync(Complaint complaint, string instructions, Guid hospitalId);
        Task NotifyAppealApprovedAsync(Appeal appeal);
        Task NotifyAppealRejectedAsync(Appeal appeal);
    }
}
