using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Services.Common;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Emergency;
using LifeLink.Services.Emergency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/emergencyrequests")]
    [Authorize(Roles = "HospitalStaff,Admin")]
    public class EmergencyRequestsController : ControllerBase
    {
        private readonly IEmergencyRequestService _emergencyRequestService;
        private readonly ICurrentUserService _currentUserService;
        private readonly AppDbContext _context;

        public EmergencyRequestsController(IEmergencyRequestService emergencyRequestService, ICurrentUserService currentUserService, AppDbContext context)
        {
            _emergencyRequestService = emergencyRequestService;
            _currentUserService = currentUserService;
            _context = context;
        }

        private Task<Guid?> CallerHospitalIdAsync() => CallerHospitalResolver.ResolveAsync(_context, _currentUserService);

        /// <summary>
        /// The signed-in hospital raises an emergency; hospitals holding compatible stock are alerted.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<EmergencyRequestResponseDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateEmergencyRequest([FromBody] EmergencyRequestCreateDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var hospitalId = await CallerHospitalIdAsync();
            if (hospitalId == null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("Your account is not linked to a hospital."));
            }
            request.HospitalId = hospitalId.Value; // a hospital raises emergencies only for itself

            try
            {
                var result = await _emergencyRequestService.CreateEmergencyRequestAsync(request);
                return CreatedAtAction(nameof(GetEmergencyRequestById), new { id = result.EmergencyRequestId }, ApiResponse<EmergencyRequestResponseDto>.Ok(result, "Emergency request created successfully."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// Retrieves all emergency blood requests.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<EmergencyRequestResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllEmergencyRequests()
        {
            var result = await _emergencyRequestService.GetAllEmergencyRequestsAsync();
            return Ok(ApiResponse<IEnumerable<EmergencyRequestResponseDto>>.Ok(result, "Emergency requests retrieved successfully."));
        }

        /// <summary>
        /// Retrieves critical priority emergency blood requests.
        /// </summary>
        [HttpGet("critical")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<EmergencyRequestResponseDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCriticalEmergencyRequests()
        {
            var result = await _emergencyRequestService.GetCriticalEmergencyRequestsAsync();
            return Ok(ApiResponse<IEnumerable<EmergencyRequestResponseDto>>.Ok(result, "Critical emergency requests retrieved successfully."));
        }

        /// <summary>
        /// Retrieves a specific emergency blood request by ID.
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<EmergencyRequestResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetEmergencyRequestById(Guid id)
        {
            var result = await _emergencyRequestService.GetEmergencyRequestAsync(id);
            if (result == null)
            {
                return NotFound(ApiResponse<object>.Fail($"Emergency request with ID '{id}' was not found."));
            }

            return Ok(ApiResponse<EmergencyRequestResponseDto>.Ok(result, "Emergency request retrieved successfully."));
        }

        /// <summary>
        /// Approves an emergency blood request.
        /// </summary>
        [HttpPut("{id:guid}/approve")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<EmergencyRequestResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ApproveEmergencyRequest(Guid id)
        {
            try
            {
                var result = await _emergencyRequestService.ApproveEmergencyRequestAsync(id, await CallerHospitalIdAsync() ?? Guid.Empty);
                return Ok(ApiResponse<EmergencyRequestResponseDto>.Ok(result, "Emergency request approved successfully."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// Rejects an emergency blood request.
        /// </summary>
        [HttpPut("{id:guid}/reject")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<EmergencyRequestResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RejectEmergencyRequest(Guid id)
        {
            try
            {
                var result = await _emergencyRequestService.RejectEmergencyRequestAsync(id, await CallerHospitalIdAsync() ?? Guid.Empty);
                return Ok(ApiResponse<EmergencyRequestResponseDto>.Ok(result, "Emergency request rejected."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// Completes an emergency blood request.
        /// </summary>
        [HttpPut("{id:guid}/complete")]
        [Authorize(Roles = "HospitalStaff")]
        [ProducesResponseType(typeof(ApiResponse<EmergencyRequestResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CompleteEmergencyRequest(Guid id)
        {
            try
            {
                var result = await _emergencyRequestService.CompleteEmergencyRequestAsync(id, await CallerHospitalIdAsync() ?? Guid.Empty, _currentUserService.UserId);
                return Ok(ApiResponse<EmergencyRequestResponseDto>.Ok(result, "Emergency request marked as completed."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Fail(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }
    }
}
