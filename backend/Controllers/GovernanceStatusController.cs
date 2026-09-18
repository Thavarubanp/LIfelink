using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Governance;
using LifeLink.Entities;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/governance/status")]
    [Authorize]
    [AllowSuspendedAccess]
    public class GovernanceStatusController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public GovernanceStatusController(AppDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        /// <summary>
        /// Retrieves the current entity's suspension notice, reason, expiry, appeal status, and allowed actions.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<GovernanceStatusDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetStatus()
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Fail("User identity could not be retrieved from token."));
            }

            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user == null)
            {
                return NotFound(ApiResponse<object>.Fail("User not found."));
            }

            // Check if user is also associated with a hospital as a doctor or staff
            var doctor = await _context.Doctors.Include(d => d.Hospital).AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId.Value);

            bool isHospitalSuspended = doctor?.Hospital != null && doctor.Hospital.IsSuspended;
            bool isUserSuspended = user.IsSuspended;

            var latestAppeal = await _context.Appeals
                .Where(a => a.UserId == user.UserId || (doctor != null && a.HospitalId == doctor.HospitalId))
                .OrderByDescending(a => a.SubmittedAt)
                .FirstOrDefaultAsync();

            var allowedActions = new List<string>
            {
                "GET /api/governance/status",
                "POST /api/appeals",
                "GET /api/appeals/my",
                "GET /api/auth/me",
                "GET /api/notifications"
            };

            if (isHospitalSuspended || doctor != null)
            {
                allowedActions.Add("POST /api/hospital/activity-reports");
            }

            var dto = new GovernanceStatusDto
            {
                IsSuspended = isUserSuspended || isHospitalSuspended,
                SuspensionReason = isUserSuspended ? user.SuspensionReason : (isHospitalSuspended ? doctor?.Hospital.SuspensionReason : null),
                SuspendedUntil = isUserSuspended ? user.SuspendedUntil : (isHospitalSuspended ? doctor?.Hospital.SuspendedUntil : null),
                AppealStatus = latestAppeal?.Status.ToString(),
                HasPendingAppeal = latestAppeal?.Status == AppealStatus.PENDING,
                SuspendedEntity = isUserSuspended ? "User" : (isHospitalSuspended ? "Hospital" : "None"),
                AllowedActions = allowedActions
            };

            return Ok(ApiResponse<GovernanceStatusDto>.Ok(dto, "Governance status retrieved successfully."));
        }
    }
}
