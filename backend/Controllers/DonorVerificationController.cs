using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.DTOs.Verification;
using LifeLink.Services.Common;
using LifeLink.Services.Verification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    /// <summary>
    /// Doctor review of AI screening reports. Only doctors decide (the assigned doctor first, any active doctor of the
    /// same hospital as fallback); AI agents can read nothing here and decide nothing.
    /// </summary>
    [ApiController]
    [Route("api/donor-verification")]
    [Authorize(Roles = "Doctor,Admin")]
    public class DonorVerificationController : ControllerBase
    {
        private readonly IVerificationService _verificationService;
        private readonly ICurrentUserService _currentUserService;

        public DonorVerificationController(IVerificationService verificationService, ICurrentUserService currentUserService)
        {
            _verificationService = verificationService;
            _currentUserService = currentUserService;
        }

        /// <summary>Approve a report version: reserves one donation slot. Optional notes are shown to the donor.</summary>
        [HttpPut("{id:guid}/approve")]
        [Authorize(Roles = "Doctor")]
        public Task<IActionResult> ApproveDonorVerification(Guid id, [FromBody] ApproveRejectRequestDto? dto) =>
            Execute(userId => _verificationService.ApproveDonorVerificationAsync(id, userId, dto?.Notes));

        /// <summary>Reject a report version with a reason the donor sees. The request stays open to other donors.</summary>
        [HttpPut("{id:guid}/reject")]
        [Authorize(Roles = "Doctor")]
        public Task<IActionResult> RejectDonorVerification(Guid id, [FromBody] ApproveRejectRequestDto dto) =>
            Execute(userId => _verificationService.RejectDonorVerificationAsync(id, userId, dto?.Notes));

        /// <summary>Report versions and decisions: a doctor's own hospital, or everything for the Admin.</summary>
        [HttpGet]
        public async Task<IActionResult> GetDonorVerifications()
        {
            try
            {
                var doctorUserId = _currentUserService.Roles.Contains("Admin") ? null : _currentUserService.UserId;
                var list = await _verificationService.GetDonorVerificationsAsync(doctorUserId);
                return Ok(list);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
        }

        private async Task<IActionResult> Execute(Func<Guid, Task<DonorVerificationResponseDto>> action)
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue)
            {
                return Unauthorized(new { message = "User identity could not be retrieved from token." });
            }

            try
            {
                return Ok(await action(userId.Value));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
