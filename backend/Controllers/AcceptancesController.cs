using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Acceptances;
using LifeLink.Services.Acceptances;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AcceptancesController : ControllerBase
    {
        private readonly IAcceptanceService _acceptanceService;
        private readonly ICurrentUserService _currentUserService;

        public AcceptancesController(
            IAcceptanceService acceptanceService,
            ICurrentUserService currentUserService)
        {
            _acceptanceService = acceptanceService;
            _currentUserService = currentUserService;
        }

        /// <summary>
        /// Donor accepts a blood request.
        /// </summary>
        [HttpPost]
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
        /// Returns all acceptances made by the current authenticated donor.
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
        /// Returns details of an acceptance by ID.
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(AcceptanceResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAcceptanceById(Guid id)
        {
            var acceptance = await _acceptanceService.GetAcceptanceByIdAsync(id);
            if (acceptance == null)
            {
                return NotFound(new { message = $"Acceptance with ID {id} was not found." });
            }

            return Ok(acceptance);
        }

        /// <summary>
        /// Donor cancels an active acceptance.
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
        /// Updates the screening status of an acceptance (Accepted -> ScreeningPending -> ScreeningCompleted -> Verified).
        /// </summary>
        [HttpPut("{id:guid}/status")]
        [ProducesResponseType(typeof(AcceptanceResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromQuery] LifeLink.Entities.AcceptanceStatus status)
        {
            try
            {
                var result = await _acceptanceService.UpdateScreeningStatusAsync(id, status);
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
    }
}
