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
        Task ProcessRequestApprovalNotificationAsync(Guid bloodRequestId, string bloodGroup, Guid hospitalId, string priority);
        Task<NotificationResponseDto> CreateRecommendationNotificationAsync(CreateRecommendationNotificationDto dto);
        Task<List<NotificationResponseDto>> GetNotificationsForUserAsync(Guid userId);
        Task<List<NotificationResponseDto>> GetAllNotificationsAsync();
        Task<int> GetUnreadCountAsync(Guid? userId, Guid? hospitalId = null);
        Task<List<NotificationResponseDto>> GetNotificationsForCallerAsync(Guid? userId, Guid? hospitalId = null);
        Task<bool> MarkNotificationReadAsync(Guid notificationId, Guid? userId, Guid? hospitalId = null, bool isAdmin = false);
        Task<bool> DeleteNotificationAsync(Guid notificationId, Guid? userId, Guid? hospitalId = null);
        Task<int> MarkAllNotificationsReadAsync(Guid? userId, Guid? hospitalId = null, bool isAdmin = false);
    }
}
