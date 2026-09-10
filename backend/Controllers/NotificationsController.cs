using System;
using System.Threading.Tasks;
using LifeLink.Services.Notification;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationAgentService _notificationService;

        public NotificationsController(INotificationAgentService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllNotifications()
        {
            var list = await _notificationService.GetAllNotificationsAsync();
            return Ok(list);
        }

        [HttpGet("user/{userId:guid}")]
        public async Task<IActionResult> GetUserNotifications(Guid userId)
        {
            var list = await _notificationService.GetNotificationsForUserAsync(userId);
            return Ok(list);
        }
    }
}
