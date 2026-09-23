using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.DTOs.Verification;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Common;
using LifeLink.Services.Hospitals;
using LifeLink.Services.Verification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    /// <summary>
    /// Blood request verification workflow:
    /// Hospital verifies (assigning a doctor) or rejects → assigned Doctor approves or rejects.
    /// Every action returns the updated blood request.
    /// </summary>
    [ApiController]
    [Route("api/requests")]
    [Authorize]
    public class RequestsVerificationController : ControllerBase
    {
        private readonly IVerificationService _verificationService;
        private readonly IBloodRequestService _bloodRequestService;
        private readonly IHospitalService _hospitalService;
        private readonly ICurrentUserService _currentUserService;

        public RequestsVerificationController(
            IVerificationService verificationService,
            IBloodRequestService bloodRequestService,
            IHospitalService hospitalService,
            ICurrentUserService currentUserService)
        {
            _verificationService = verificationService;
            _bloodRequestService = bloodRequestService;
            _hospitalService = hospitalService;
            _currentUserService = currentUserService;
        }

        /// <summary>
        /// Hospital verifies a pending request and assigns one of its doctors (DoctorId is mandatory).
        /// </summary>
        [HttpPut("{id:guid}/verify")]
        [Authorize(Roles = "HospitalStaff")]
        public async Task<IActionResult> VerifyRequest(Guid id, [FromBody] ApproveRejectRequestDto dto)
        {
            return await Execute(id, async () =>
            {
                var hospitalId = await RequireHospitalIdAsync();
                await _verificationService.VerifyBloodRequestAsync(id, hospitalId, dto.DoctorId);
            });
        }

        /// <summary>
        /// Assigned doctor approves a verified request. Optional Notes are stored with the decision.
        /// </summary>
        [HttpPut("{id:guid}/approve")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> ApproveRequest(Guid id, [FromBody] ApproveRejectRequestDto? dto)
        {
            return await Execute(id, () =>
                _verificationService.ApproveBloodRequestAsync(id, RequireUserId(), dto?.Notes));
        }

        /// <summary>
        /// Rejects a request with a mandatory message (Notes).
        /// Hospital staff may reject requests sent to their hospital; doctors may reject requests assigned to them.
        /// </summary>
        [HttpPut("{id:guid}/reject")]
        [Authorize(Roles = "HospitalStaff,Doctor")]
        public async Task<IActionResult> RejectRequest(Guid id, [FromBody] ApproveRejectRequestDto dto)
        {
            return await Execute(id, async () =>
            {
                if (_currentUserService.Roles.Contains("HospitalStaff"))
                {
                    var hospitalId = await RequireHospitalIdAsync();
                    await _verificationService.RejectBloodRequestByHospitalAsync(id, hospitalId, dto.Notes);
                }
                else
                {
                    await _verificationService.RejectBloodRequestAsync(id, RequireUserId(), dto.Notes);
                }
            });
        }

        /// <summary>
        /// Raw verification audit list (admin oversight only).
        /// </summary>
        [HttpGet("verifications")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetVerifications()
        {
            var list = await _verificationService.GetBloodRequestVerificationsAsync();
            return Ok(list);
        }

        private async Task<IActionResult> Execute(Guid requestId, Func<Task> action)
        {
            try
            {
                await action();
                var updated = await _bloodRequestService.GetRequestByIdAsync(requestId);
                return Ok(updated);
            }
            catch (UnauthorizedAccessException ex)
            {
                // 403, not 401: the caller is signed in but not allowed to act on this request
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

        private Guid RequireUserId()
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue || userId.Value == Guid.Empty)
            {
                throw new UnauthorizedAccessException("User identity could not be retrieved from token.");
            }
            return userId.Value;
        }

        private async Task<Guid> RequireHospitalIdAsync()
        {
            var hospitalId = await _hospitalService.GetHospitalIdByEmailAsync(_currentUserService.Email);
            if (hospitalId == null)
            {
                throw new UnauthorizedAccessException("Your account is not linked to a hospital.");
            }
            return hospitalId.Value;
        }
    }
}
