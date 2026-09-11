using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.DTOs.Acceptances;
using LifeLink.DTOs.BloodRequests;
using LifeLink.Services.Acceptances;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BloodRequestsController : ControllerBase
    {
        private readonly IBloodRequestService _bloodRequestService;
        private readonly IAcceptanceService _acceptanceService;
        private readonly ICurrentUserService _currentUserService;

        public BloodRequestsController(
            IBloodRequestService bloodRequestService,
            IAcceptanceService acceptanceService,
            ICurrentUserService currentUserService)
        {
            _bloodRequestService = bloodRequestService;
            _acceptanceService = acceptanceService;
            _currentUserService = currentUserService;
        }

        /// <summary>
        /// Patient creates a blood request.
        /// </summary>
        [HttpPost]
        [Authorize]
        [ProducesResponseType(typeof(BloodRequestResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreateRequest([FromBody] CreateBloodRequestDto dto)
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue || userId.Value == Guid.Empty)
            {
                return Unauthorized(new { message = "User identity could not be retrieved from token." });
            }

            try
            {
                var result = await _bloodRequestService.CreateRequestAsync(userId.Value, dto);
                return CreatedAtAction(nameof(GetRequestById), new { id = result.BloodRequestId }, result);
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
        /// Returns blood requests created by the current user.
        /// </summary>
        [HttpGet("my")]
        [Authorize]
        [ProducesResponseType(typeof(IEnumerable<BloodRequestResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetMyRequests()
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue || userId.Value == Guid.Empty)
            {
                return Unauthorized(new { message = "User identity could not be retrieved from token." });
            }

            var requests = await _bloodRequestService.GetMyRequestsAsync(userId.Value);
            return Ok(requests);
        }

        /// <summary>
        /// Returns public active approved blood requests for donors and agents.
        /// </summary>
        [HttpGet("public")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(IEnumerable<BloodRequestResponseDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPublicRequests(
            [FromQuery] string? bloodGroup = null,
            [FromQuery] int? expiringWithinHours = null)
        {
            var requests = await _bloodRequestService.GetPublicRequestsAsync(bloodGroup, expiringWithinHours);
            return Ok(requests);
        }

        /// <summary>
        /// Returns pending blood requests for hospital verification dashboard.
        /// </summary>
        [HttpGet("pending")]
        [Authorize]
        [ProducesResponseType(typeof(IEnumerable<BloodRequestResponseDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPendingRequests([FromQuery] Guid? hospitalId = null)
        {
            var requests = await _bloodRequestService.GetPendingRequestsAsync(hospitalId);
            return Ok(requests);
        }

        /// <summary>
        /// Returns request details by ID.
        /// </summary>
        [HttpGet("{id:guid}")]
        [Authorize]
        [ProducesResponseType(typeof(BloodRequestResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetRequestById(Guid id)
        {
            var request = await _bloodRequestService.GetRequestByIdAsync(id);
            if (request == null)
            {
                return NotFound(new { message = $"Blood request with ID {id} was not found." });
            }

            return Ok(request);
        }

        /// <summary>
        /// Creator cancels the blood request.
        /// </summary>
        [HttpPut("{id:guid}/cancel")]
        [Authorize]
        [ProducesResponseType(typeof(BloodRequestResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CancelRequest(Guid id)
        {
            var userId = _currentUserService.UserId;
            if (!userId.HasValue || userId.Value == Guid.Empty)
            {
                return Unauthorized(new { message = "User identity could not be retrieved from token." });
            }

            try
            {
                var result = await _bloodRequestService.CancelRequestAsync(id, userId.Value);
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
        /// Returns all acceptances and donor ranking details for a request.
        /// </summary>
        [HttpGet("{id:guid}/acceptances")]
        [Authorize]
        [ProducesResponseType(typeof(List<RequestAcceptanceDetailDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRequestAcceptances(Guid id)
        {
            var list = await _acceptanceService.GetRequestAcceptancesAsync(id);
            return Ok(list);
        }

        /// <summary>
        /// Doctor/Hospital staff finalizes donor selection for a blood request.
        /// </summary>
        [HttpPut("{id:guid}/finalize-selection")]
        [Authorize]
        [ProducesResponseType(typeof(FinalizeDonorSelectionResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> FinalizeDonorSelection(Guid id, [FromBody] FinalizeDonorSelectionDto dto)
        {
            var doctorUserId = _currentUserService.UserId ?? Guid.Empty;

            try
            {
                var result = await _acceptanceService.FinalizeDonorSelectionAsync(id, dto.SelectedAcceptanceIds, doctorUserId);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
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
        /// Returns blood request fulfillment analytics for LangGraph agent and dashboards.
        /// </summary>
        [HttpGet("{id:guid}/analytics")]
        [Authorize]
        [ProducesResponseType(typeof(BloodRequestAnalyticsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetRequestAnalytics(Guid id)
        {
            try
            {
                var analytics = await _bloodRequestService.GetRequestAnalyticsAsync(id);
                return Ok(analytics);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Returns fulfillment history audit trail for a blood request.
        /// </summary>
        [HttpGet("{id:guid}/fulfillment-history")]
        [Authorize]
        [ProducesResponseType(typeof(IEnumerable<RequestFulfillmentHistoryResponseDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRequestFulfillmentHistory(Guid id)
        {
            var history = await _bloodRequestService.GetRequestFulfillmentHistoryAsync(id);
            return Ok(history);
        }
    }
}
