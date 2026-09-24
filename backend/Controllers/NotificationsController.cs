using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Notification;
using LifeLink.Services.Common;
using LifeLink.Services.Notification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationAgentService _notificationService;
        private readonly ICurrentUserService _currentUserService;
        private readonly AppDbContext _context;

        public NotificationsController(
            INotificationAgentService notificationService,
            ICurrentUserService currentUserService,
            AppDbContext context)
        {
            _notificationService = notificationService;
            _currentUserService = currentUserService;
            _context = context;
        }

        private async Task<(Guid? UserId, Guid? HospitalId, bool IsAdmin)> ResolveCallerAsync()
        {
            var userId = _currentUserService.UserId;
            bool isAdmin = _currentUserService.Roles.Contains("Admin");
            Guid? hospitalId = null;

            if (_currentUserService.Roles.Contains("HospitalStaff") && !string.IsNullOrWhiteSpace(_currentUserService.Email))
            {
                var hospital = await _context.Hospitals
                    .FirstOrDefaultAsync(h => h.Email != null && h.Email.ToLower() == _currentUserService.Email.ToLower());
                if (hospital != null)
                {
                    hospitalId = hospital.HospitalId;
                }
            }

            return (userId, hospitalId, isAdmin);
        }

        /// <summary>Every notification in the system (all users). Admin only.</summary>
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllNotifications()
        {
            var list = await _notificationService.GetAllNotificationsAsync();
            return Ok(list);
        }

        [HttpGet("my")]
        [Authorize]
        public async Task<IActionResult> GetMyNotifications()
        {
            var caller = await ResolveCallerAsync();
            var list = await _notificationService.GetNotificationsForCallerAsync(caller.UserId, caller.HospitalId);
            return Ok(list);
        }

        [HttpGet("unread-count")]
        [Authorize]
        public async Task<IActionResult> GetUnreadCount()
        {
            var caller = await ResolveCallerAsync();
            int count = await _notificationService.GetUnreadCountAsync(caller.UserId, caller.HospitalId);
            return Ok(new { count });
        }

        [HttpPatch("{id:guid}/read")]
        [Authorize]
        public async Task<IActionResult> MarkAsRead(Guid id)
        {
            var caller = await ResolveCallerAsync();
            bool success = await _notificationService.MarkNotificationReadAsync(id, caller.UserId, caller.HospitalId, caller.IsAdmin);
            if (!success)
            {
                return NotFound(ApiResponse<object>.Fail("Notification not found or access denied."));
            }
            return Ok(ApiResponse<object>.Ok(new object(), "Notification marked as read."));
        }

        /// <summary>
        /// Dismisses (permanently deletes) one of the caller's own notifications.
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize]
        public async Task<IActionResult> Dismiss(Guid id)
        {
            var caller = await ResolveCallerAsync();
            bool success = await _notificationService.DeleteNotificationAsync(id, caller.UserId, caller.HospitalId);
            if (!success)
            {
                return NotFound(ApiResponse<object>.Fail("Notification not found or access denied."));
            }
            return Ok(ApiResponse<object>.Ok(new object(), "Notification dismissed."));
        }

        [HttpPatch("read-all")]
        [Authorize]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var caller = await ResolveCallerAsync();
            int count = await _notificationService.MarkAllNotificationsReadAsync(caller.UserId, caller.HospitalId, caller.IsAdmin);
            return Ok(new { count, message = $"Marked {count} notifications as read." });
        }

        /// <summary>A user's notifications: only that user, the Admin, or internal agent services.</summary>
        [HttpGet("user/{userId:guid}")]
        [Authorize]
        public async Task<IActionResult> GetUserNotifications(Guid userId)
        {
            var roles = _currentUserService.Roles;
            if (_currentUserService.UserId != userId && !roles.Contains("Admin") && !roles.Contains("InternalAgent"))
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("You can only view your own notifications."));
            }

            var list = await _notificationService.GetNotificationsForUserAsync(userId);
            return Ok(list);
        }

        /// <summary>
        /// Creates a recommendation notification generated by AI Agents (authenticated with X-Internal-Key) or the Admin.
        /// </summary>
        [HttpPost("recommendations")]
        [Authorize(Roles = "Admin,InternalAgent")]
        [ProducesResponseType(typeof(ApiResponse<NotificationResponseDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateRecommendationNotification([FromBody] CreateRecommendationNotificationDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _notificationService.CreateRecommendationNotificationAsync(request);
            return CreatedAtAction(
                nameof(GetAllNotifications),
                new { id = result.NotificationId },
                ApiResponse<NotificationResponseDto>.Ok(result, "Recommendation notification created successfully.")
            );
        }
    }
}
