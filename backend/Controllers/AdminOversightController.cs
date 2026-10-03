using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.ActivityLogs;
using LifeLink.DTOs.Admin;
using LifeLink.DTOs.BloodRequests;
using LifeLink.DTOs.Common;
using LifeLink.Services.Admin;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    /// <summary>
    /// Admin oversight: activity logs of users and hospitals, every blood request, the badge counts of the admin sidebar
    /// and dashboard (read-only), plus Suspend / Lift of a blood request or transfer and one-way messages (Phase 3B).
    /// The admin never edits or deletes requests or transfers. All transfers come from the existing GET /api/transfers.
    /// </summary>
    [ApiController]
    [Route("api/Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminOversightController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IAdminService _adminService;
        private readonly IBloodRequestService _bloodRequestService;
        private readonly ICurrentUserService _currentUserService;
        private readonly IAdminOversightActionsService _actions;

        public AdminOversightController(AppDbContext context, IAdminService adminService, IBloodRequestService bloodRequestService,
            ICurrentUserService currentUserService, IAdminOversightActionsService actions)
        {
            _context = context;
            _adminService = adminService;
            _bloodRequestService = bloodRequestService;
            _currentUserService = currentUserService;
            _actions = actions;
        }

        private Guid AdminId => _currentUserService.UserId ?? throw new UnauthorizedAccessException("Admin identity could not be retrieved from token.");

        /// <summary>Suspends an open blood request: nobody can act on it except a donor withdrawing and the creator deleting (Q7).</summary>
        [HttpPut("blood-requests/{id:guid}/suspend")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> SuspendBloodRequest(Guid id, [FromBody] AdminSuspendItemDto dto)
        {
            await _actions.SuspendRequestAsync(id, AdminId, dto?.Reason);
            return NoContent();
        }

        [HttpPut("blood-requests/{id:guid}/lift")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> LiftBloodRequest(Guid id)
        {
            await _actions.LiftRequestAsync(id, AdminId);
            return NoContent();
        }

        /// <summary>Suspends a pending transfer: it cannot be accepted, rejected or withdrawn until lifted.</summary>
        [HttpPut("transfers/{id:guid}/suspend")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> SuspendTransfer(Guid id, [FromBody] AdminSuspendItemDto dto)
        {
            await _actions.SuspendTransferAsync(id, AdminId, dto?.Reason);
            return NoContent();
        }

        [HttpPut("transfers/{id:guid}/lift")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> LiftTransfer(Guid id)
        {
            await _actions.LiftTransferAsync(id, AdminId);
            return NoContent();
        }

        /// <summary>One-way "Message from Administrator" to one user or one hospital (doctors via their hospital). No replies.</summary>
        [HttpPost("messages")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SendMessage([FromBody] AdminMessageDto dto)
        {
            await _actions.SendMessageAsync(AdminId, dto);
            return NoContent();
        }

        /// <summary>A user's full activity log (what they did and what was done to their account), paged and filtered.</summary>
        [HttpGet("users/{id:guid}/activity-log")]
        [ProducesResponseType(typeof(ActivityLogPageDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetUserActivityLog(Guid id, [FromQuery] ActivityLogQueryDto query)
        {
            if (!await _context.Users.AnyAsync(u => u.UserId == id))
            {
                return NotFound(ApiResponse<object>.Fail("User not found."));
            }
            return Ok(await ActivityLogQueries.PageAsync(ActivityLogQueries.ForUser(_context, id), query, viewerIsAdmin: true));
        }

        /// <summary>A hospital's full activity log (its staff, its doctors, admin actions on it), paged and filtered.</summary>
        [HttpGet("hospitals/{id:guid}/activity-log")]
        [ProducesResponseType(typeof(ActivityLogPageDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetHospitalActivityLog(Guid id, [FromQuery] ActivityLogQueryDto query)
        {
            if (!await _context.Hospitals.AnyAsync(h => h.HospitalId == id))
            {
                return NotFound(ApiResponse<object>.Fail("Hospital not found."));
            }
            return Ok(await ActivityLogQueries.PageAsync(ActivityLogQueries.ForHospital(_context, id), query, viewerIsAdmin: true));
        }

        /// <summary>Every blood request (users', hospitals' and the Admin's, any status including deleted), newest first. Read-only.</summary>
        [HttpGet("blood-requests")]
        [ProducesResponseType(typeof(IEnumerable<BloodRequestResponseDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllBloodRequests() => Ok(await _bloodRequestService.GetAllRequestsForAdminAsync());

        /// <summary>
        /// Badge counts: new blood requests and transfers since the admin last opened that Activity log tab, and pending
        /// hospital registrations, appeals and complaints. Polled in the background (does not extend the session).
        /// </summary>
        [HttpGet("attention-counts")]
        [ProducesResponseType(typeof(AdminAttentionDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAttentionCounts() =>
            Ok(await _adminService.GetAttentionCountsAsync(_currentUserService.UserId ?? Guid.Empty));

        /// <summary>The admin opened an Activity log tab (area: blood-requests or transfers): its "new" highlight clears.</summary>
        [HttpPut("attention/{area}/seen")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> MarkSeen(string area)
        {
            if (!AdminService.AttentionAreas.IsValid(area))
            {
                return BadRequest(ApiResponse<object>.Fail($"Unknown area '{area}'."));
            }
            await _adminService.MarkAreaSeenAsync(_currentUserService.UserId ?? Guid.Empty, area);
            return NoContent();
        }
    }
}
