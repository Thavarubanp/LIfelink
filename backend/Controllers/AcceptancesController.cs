using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        [Authorize(Roles = "User")]
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
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Returns all acceptances made by the current donor, with request details and the screening decision history.
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

            var result = await _acceptanceService.GetMyAcceptancesAsync(userId.Value);
            return Ok(result);
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

            try
            {
                var result = await _acceptanceService.CancelAcceptanceAsync(id, userId.Value);
                return Ok(result);
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
            catch (InvalidOperationException ex)
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
            catch (InvalidOperationException ex)
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
            return await _context.BloodRequests.AnyAsync(r => r.BloodRequestId == acceptance.BloodRequestId && r.HospitalId == callerHospitalId);
        }
    }
}
