using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.ActivityLogs;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    /// <summary>The signed-in account's own activity log ("my activity"); the Admin reads others' logs via /api/Admin.</summary>
    [ApiController]
    [Route("api/activity-logs")]
    [Authorize]
    public class ActivityLogsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public ActivityLogsController(AppDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        /// <summary>
        /// Own activity, paged and filtered by type and date: hospital staff see their hospital's log (staff, its doctors and
        /// admin actions on it); everyone else sees what they did and what was done to their account.
        /// </summary>
        [HttpGet("my")]
        [ProducesResponseType(typeof(ActivityLogPageDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyActivity([FromQuery] ActivityLogQueryDto query)
        {
            var userId = _currentUserService.UserId;
            if (userId == null) return Unauthorized();

            var roles = _currentUserService.Roles.ToList();
            if (roles.Contains("HospitalStaff") && !roles.Contains("Admin"))
            {
                var hospitalId = await CallerHospitalResolver.ResolveAsync(_context, _currentUserService);
                if (hospitalId == null) return StatusCode(StatusCodes.Status403Forbidden, new { message = "Your account is not linked to a hospital." });
                return Ok(await ActivityLogQueries.PageAsync(ActivityLogQueries.ForHospital(_context, hospitalId.Value), query, viewerIsAdmin: false));
            }

            return Ok(await ActivityLogQueries.PageAsync(ActivityLogQueries.ForUser(_context, userId.Value), query, viewerIsAdmin: roles.Contains("Admin")));
        }
    }
}
