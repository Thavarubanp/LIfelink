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
        Task NotifyComplaintCreatorAsync(Complaint complaint, string title, string message);
        Task NotifyComplaintAdminsAsync(Complaint complaint, string title, string message);
        Task NotifyAdminAsync(string title, string message);
        Task NotifyUserAsync(Guid? userId, Guid? hospitalId, string title, string message);
        Task NotifyAppealApprovedAsync(Appeal appeal);
        Task NotifyAppealRejectedAsync(Appeal appeal);
    }
}
