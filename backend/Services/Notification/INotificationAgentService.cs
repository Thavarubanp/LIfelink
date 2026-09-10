using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Notification;

namespace LifeLink.Services.Notification
{
    public interface INotificationAgentService
    {
        Task<int> NotifyEligibleDonorsAsync(Guid bloodRequestId, string bloodGroup, Guid hospitalId, string priority);
        Task<int> NotifyUrgentHospitalsAsync(Guid bloodRequestId, string bloodGroup, Guid requestingHospitalId, string priority);
        Task<int> NotifyAdminsAsync(Guid bloodRequestId, string bloodGroup, Guid hospitalId, string priority);
        Task ProcessRequestApprovalNotificationAsync(Guid bloodRequestId, string bloodGroup, Guid hospitalId, string priority);
        Task<List<NotificationResponseDto>> GetNotificationsForUserAsync(Guid userId);
        Task<List<NotificationResponseDto>> GetAllNotificationsAsync();
    }
}
