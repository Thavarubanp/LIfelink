using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Governance;
using LifeLink.Entities;
using LifeLink.Services.Appeals;
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
        private readonly IAppealService _appealService;

        public GovernanceStatusController(AppDbContext context, ICurrentUserService currentUserService, IAppealService appealService)
        {
            _context = context;
            _currentUserService = currentUserService;
            _appealService = appealService;
        }

        /// <summary>
        /// Governance Portal data for the caller: profile summary, suspension reason, appeal threads and what they may do.
        /// Suspended users and staff of a suspended hospital can appeal and reply; doctors of a suspended hospital only view.
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

            var user = await _context.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user == null)
            {
                return NotFound(ApiResponse<object>.Fail("User not found."));
            }

            var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
            var hospital = await GovernanceAccessHelper.GetGovernedHospitalAsync(_context, user, roles);
            var isDoctor = roles.Contains("Doctor");
            bool isUserSuspended = user.IsSuspended;
            bool isHospitalSuspended = hospital?.IsSuspended == true;

            // Oldest -> newest; each appeal carries its message thread
            var allAppeals = (await _appealService.GetMyAppealsAsync(user.UserId)).OrderBy(a => a.SubmittedAt).ToList();
            var latestAppeal = allAppeals.LastOrDefault();
            var hasOpenThread = allAppeals.Any(a => !a.IsClosed);

            var dto = new GovernanceStatusDto
            {
                IsSuspended = isUserSuspended || isHospitalSuspended,
                IsPermanentlyBlocked = user.AccountStatus == AccountStatus.Blocked,
                SuspensionReason = isUserSuspended ? user.SuspensionReason : (isHospitalSuspended ? hospital!.SuspensionReason : null),
                SuspendedUntil = isUserSuspended ? user.SuspendedUntil : (isHospitalSuspended ? hospital!.SuspendedUntil : null),
                AppealStatus = latestAppeal?.Status,
                HasPendingAppeal = latestAppeal?.Status == nameof(AppealStatus.PENDING),
                SuspendedEntity = isUserSuspended ? "User" : (isHospitalSuspended ? "Hospital" : "None"),
                AllowedActions = new List<string> { "GET /api/governance/status", "POST /api/auth/logout" },
                AllAppeals = allAppeals,
                IsReadOnlyViewer = isDoctor,
                CanAppeal = !isDoctor && (isUserSuspended || isHospitalSuspended) && !hasOpenThread,
                Profile = new GovernanceProfileSummaryDto
                {
                    Name = $"{user.FirstName} {user.LastName}".Trim(),
                    Email = user.Email,
                    Role = roles.FirstOrDefault() ?? "User",
                    Phone = user.PhoneNumber,
                    Status = isUserSuspended || isHospitalSuspended ? "Suspended" : user.AccountStatus.ToString(),
                    CreatedAt = user.CreatedAt,
                    HospitalName = hospital?.Name
                }
            };

            if (!isDoctor)
            {
                dto.AllowedActions.Add("POST /api/appeals");
                dto.AllowedActions.Add("POST /api/appeals/{id}/reply");
            }

            return Ok(ApiResponse<GovernanceStatusDto>.Ok(dto, "Governance status retrieved successfully."));
        }
    }
}
