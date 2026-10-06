using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.Services.Acceptances;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AcceptancesController : ControllerBase
    {
        private readonly IAcceptanceService _acceptanceService;
        private readonly ICurrentUserService _currentUserService;
        private readonly AppDbContext _context;

        public AcceptancesController(
            IAcceptanceService acceptanceService,
            ICurrentUserService currentUserService,
            AppDbContext context)
        {
            _acceptanceService = acceptanceService;
            _currentUserService = currentUserService;
            _context = context;
        }

        /// <summary>
        /// Donor accepts a blood request. Only donor/patient accounts take part in donation.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "User,Admin")]
        [ProducesResponseType(typeof(AcceptanceResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> AcceptRequest([FromBody] CreateAcceptanceDto dto)
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue || userId.Value == Guid.Empty)
            {
                return Unauthorized(new { message = "User identity could not be retrieved from token." });
            }

            try
            {
                var result = await _acceptanceService.AcceptRequestAsync(userId.Value, dto);
                return CreatedAtAction(nameof(GetAcceptanceById), new { id = result.AcceptanceId }, result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ConflictException ex)
            {
                return StatusCode(StatusCodes.Status409Conflict, new { message = ex.Message });
            }
            catch (InvalidOperationException ex) when (ex is not ConflictException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// A hospital accepts a public blood request by donating selected Available packets of the required blood group
        /// from its own inventory. No AI agent runs; the request's assigned doctor approves or rejects.
        /// </summary>
        [HttpPost("hospital")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(AcceptanceResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public Task<IActionResult> AcceptAsHospital([FromBody] CreateHospitalDonationDto dto) =>
            ExecuteAsync(async () =>
            {
                var hospitalId = await CallerHospitalIdAsync();
                var result = await _acceptanceService.AcceptAsHospitalAsync(_currentUserService.UserId!.Value, hospitalId, dto);
                return CreatedAtAction(nameof(GetAcceptanceById), new { id = result.AcceptanceId }, result);
            });

        /// <summary>The request's assigned doctor approves a hospital donation; the units are fulfilled at once.</summary>
        [HttpPut("{id:guid}/hospital-approve")]
        [Authorize(Roles = "Doctor")]
        public Task<IActionResult> ApproveHospitalDonation(Guid id, [FromBody] HospitalDonationDecisionDto? dto = null) =>
            ExecuteAsync(async () => Ok(await _acceptanceService.ApproveHospitalDonationAsync(id, _currentUserService.UserId!.Value, dto?.Notes)));

        /// <summary>The request's assigned doctor rejects a hospital donation with a reason; the packets return.</summary>
        [HttpPut("{id:guid}/hospital-reject")]
        [Authorize(Roles = "Doctor")]
        public Task<IActionResult> RejectHospitalDonation(Guid id, [FromBody] HospitalDonationDecisionDto? dto = null) =>
            ExecuteAsync(async () => Ok(await _acceptanceService.RejectHospitalDonationAsync(id, _currentUserService.UserId!.Value, dto?.Reason)));

        /// <summary>
        /// Returns all acceptances made by the current donor, with request details and the screening decision history.
        /// For hospital staff: the donations their hospital offered, with the packets.
        /// </summary>
        [HttpGet("my")]
        [ProducesResponseType(typeof(IEnumerable<AcceptanceResponseDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyAcceptances()
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue || userId.Value == Guid.Empty)
            {
                return Unauthorized(new { message = "User identity could not be retrieved from token." });
            }

            if (_currentUserService.Roles.Contains("HospitalStaff"))
            {
                var hospitalId = await CallerHospitalResolver.ResolveAsync(_context, _currentUserService);
                if (hospitalId == null)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { message = "Your account is not linked to a hospital." });
                }
                return Ok(await _acceptanceService.GetHospitalDonationsAsync(hospitalId.Value));
            }

            var result = await _acceptanceService.GetMyAcceptancesAsync(userId.Value);
            return Ok(result);
        }

        /// <summary>
        /// Narrow governance exception: a suspended plain donor can see only their own active participation records so
        /// they can end an existing clinical commitment. It does not expose closed history or enable any new workflow.
        /// </summary>
        [HttpGet("my-active-withdrawals")]
        [Authorize(Roles = "User,Admin")]
        [AllowSuspendedAccess]
        public async Task<IActionResult> GetMyActiveWithdrawals()
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue || userId.Value == Guid.Empty) return Unauthorized();
            if (!_currentUserService.Roles.Any(r => r is "User" or "Admin") ||
                _currentUserService.Roles.Any(r => r is "HospitalStaff" or "Doctor"))
            {
                return Forbid();
            }
            var active = new[] { "Accepted", "ScreeningPending", "ScreeningCompleted", "Verified" };
            return Ok((await _acceptanceService.GetMyAcceptancesAsync(userId.Value)).Where(a => active.Contains(a.Status)));
        }

        /// <summary>
        /// Returns an acceptance to its donor, the request hospital's doctors and staff, the Admin or the screening agent.
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(AcceptanceResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAcceptanceById(Guid id)
        {
            var acceptance = await _acceptanceService.GetAcceptanceByIdAsync(id);
            if (acceptance == null || !await CanViewAsync(acceptance))
            {
                return NotFound(new { message = $"Acceptance with ID {id} was not found." });
            }

            return Ok(acceptance);
        }

        /// <summary>
        /// Donor withdraws. After a doctor's approval the reserved donation slot becomes available again.
        /// </summary>
        [HttpPut("{id:guid}/cancel")]
        [Authorize(Roles = "User,HospitalStaff,Admin")]
        [ProducesResponseType(typeof(AcceptanceResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CancelAcceptance(Guid id)
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue || userId.Value == Guid.Empty)
            {
                return Unauthorized(new { message = "User identity could not be retrieved from token." });
            }

            if (_currentUserService.Roles.Contains("Doctor"))
            {
                return Forbid();
            }

            if (_currentUserService.Roles.Contains("HospitalStaff"))
            {
                // A hospital withdraws its own donation offer; the held packets return to its inventory
                return await ExecuteAsync(async () => Ok(await _acceptanceService.WithdrawHospitalDonationAsync(id, await CallerHospitalIdAsync())));
            }

            try
            {
                var result = await _acceptanceService.CancelAcceptanceAsync(id, userId.Value);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex) when (ex is not ConflictException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Narrow governance exception allowing a suspended donor to withdraw only their own active acceptance.</summary>
        [HttpPut("{id:guid}/suspended-withdraw")]
        [Authorize(Roles = "User,Admin")]
        [AllowSuspendedAccess]
        public async Task<IActionResult> WithdrawWhileSuspended(Guid id)
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue || userId.Value == Guid.Empty)
            {
                return Unauthorized(new { message = "User identity could not be retrieved from token." });
            }
            if (!_currentUserService.Roles.Any(r => r is "User" or "Admin") ||
                _currentUserService.Roles.Any(r => r is "HospitalStaff" or "Doctor"))
            {
                return Forbid();
            }

            try
            {
                return Ok(await _acceptanceService.CancelAcceptanceAsync(id, userId.Value));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex) when (ex is not ConflictException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>7.3: the donor's own submitted screening answers (latest version; no AI risk level, flags or summary).</summary>
        [HttpGet("{id:guid}/screening-answers")]
        [ProducesResponseType(typeof(ScreeningAnswersDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetScreeningAnswers(Guid id)
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue) return Unauthorized();
            try
            {
                return Ok(await _acceptanceService.GetScreeningAnswersAsync(id, userId.Value));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
        }

        /// <summary>
        /// 7.2: the donor saves the edit form while their report waits for the doctor. The answers are checked first; the
        /// current version is then superseded and the new one is built in the background (no chat). 400 lists what to fix.
        /// </summary>
        [HttpPut("{id:guid}/screening-answers")]
        [ProducesResponseType(typeof(AcceptanceResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> UpdateScreeningAnswers(Guid id, [FromBody] UpdateScreeningAnswersDto dto)
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue) return Unauthorized();
            try
            {
                return Ok(await _acceptanceService.UpdateScreeningAnswersAsync(id, userId.Value, dto.Answers));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (ScreeningAgentUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
            }
            catch (InvalidOperationException ex) when (ex is not LifeLink.Common.ConflictException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Screening status changes. The Request Management agent opens the interview (Accepted → ScreeningPending);
        /// the donor reopens their own answers while the report awaits the doctor (ScreeningCompleted → ScreeningPending,
        /// which supersedes that report version). Doctor decisions are made on /api/donor-verification only.
        /// </summary>
        [HttpPut("{id:guid}/status")]
        [ProducesResponseType(typeof(AcceptanceResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromQuery] LifeLink.Entities.AcceptanceStatus status)
        {
            try
            {
                if (_currentUserService.Roles.Contains("InternalAgent"))
                {
                    return Ok(await _acceptanceService.UpdateScreeningStatusAsync(id, status));
                }

                var userId = _currentUserService.UserId;
                if (status == LifeLink.Entities.AcceptanceStatus.ScreeningPending && userId.HasValue)
                {
                    return Ok(await _acceptanceService.ReopenScreeningAsync(id, userId.Value));
                }

                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You cannot change this acceptance status." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex) when (ex is not ConflictException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// A doctor or the hospital's staff releases an acceptance that cannot proceed (no-show, suspended donor).
        /// A reserved slot becomes available again. A reason is required and shown to the donor.
        /// </summary>
        [HttpPut("{id:guid}/release")]
        [Authorize(Roles = "Doctor,HospitalStaff")]
        [ProducesResponseType(typeof(AcceptanceResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> ReleaseReservation(Guid id, [FromBody] ReleaseReservationDto dto)
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue)
            {
                return Unauthorized(new { message = "User identity could not be retrieved from token." });
            }

            try
            {
                Guid? actingHospitalId = null;
                if (_currentUserService.Roles.Contains("HospitalStaff"))
                {
                    actingHospitalId = await CallerHospitalResolver.ResolveAsync(_context, _currentUserService)
                                       ?? throw new UnauthorizedAccessException("Your account is not linked to a hospital.");
                }

                return Ok(await _acceptanceService.ReleaseReservationAsync(id, userId.Value, actingHospitalId, dto?.Reason));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex) when (ex is not ConflictException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private async Task<Guid> CallerHospitalIdAsync() =>
            await CallerHospitalResolver.ResolveAsync(_context, _currentUserService)
            ?? throw new UnauthorizedAccessException("Your account is not linked to a hospital.");

        private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ConflictException ex)
            {
                return StatusCode(StatusCodes.Status409Conflict, new { message = ex.Message });
            }
            catch (InvalidOperationException ex) when (ex is not ConflictException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private async Task<bool> CanViewAsync(AcceptanceResponseDto acceptance)
        {
            var roles = _currentUserService.Roles.ToList();
            if (roles.Contains("Admin") || roles.Contains("InternalAgent")) return true;
            if (_currentUserService.UserId == acceptance.DonorUserId) return true;

            var callerHospitalId = await CallerHospitalResolver.ResolveAsync(_context, _currentUserService);
            if (callerHospitalId == null) return false;
            if (acceptance.DonorHospitalId == callerHospitalId) return true; // the donating hospital
            return await _context.BloodRequests.AnyAsync(r => r.BloodRequestId == acceptance.BloodRequestId && r.HospitalId == callerHospitalId);
        }
    }
}
