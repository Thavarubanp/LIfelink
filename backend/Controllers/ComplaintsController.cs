using System;
using System.Threading.Tasks;
using LifeLink.DTOs.Common;
using LifeLink.DTOs.Complaints;
using LifeLink.Services.Common;
using LifeLink.Services.Complaints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "User,HospitalStaff")] // only Users and Hospital Staff create/own complaints (no doctors, no admins)
    public class ComplaintsController : ControllerBase
    {
        private readonly IComplaintService _complaintService;
        private readonly ICurrentUserService _currentUserService;

        public ComplaintsController(IComplaintService complaintService, ICurrentUserService currentUserService)
        {
            _complaintService = complaintService;
            _currentUserService = currentUserService;
        }

        /// <summary>
        /// Submits a complaint or feedback regarding the platform or hospital operations.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResponse<ComplaintResponseDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreateComplaint([FromBody] CreateComplaintDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = _currentUserService.UserId;
            try
            {
                var result = await _complaintService.CreateComplaintAsync(userId, request.HospitalId, request);
                return StatusCode(StatusCodes.Status201Created, ApiResponse<ComplaintResponseDto>.Ok(AdminIdentityRedaction.ForComplainant(result), "Complaint submitted successfully."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        /// <summary>
        /// Creator reply with an optional attachment. Allowed only after an admin reply (replies alternate).
        /// </summary>
        [HttpPost("{id:guid}/reply")]
        [ProducesResponseType(typeof(ApiResponse<ComplaintResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ReplyToComplaint(Guid id, [FromBody] ReviewComplaintDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = _currentUserService.UserId;
            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.Fail("User identity could not be retrieved from token."));
            }

            try
            {
                var result = await _complaintService.CreatorReplyAsync(id, userId.Value, request);
                return Ok(ApiResponse<ComplaintResponseDto>.Ok(AdminIdentityRedaction.ForComplainant(result), "Reply sent."));
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
        /// Gets all complaints submitted by the currently authenticated user.
        /// </summary>
        [HttpGet("my-complaints")]
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<System.Collections.Generic.List<ComplaintResponseDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetMyComplaints()
        {
            var userId = _currentUserService.UserId;
            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.Fail("User identity could not be retrieved from token."));
            }

            var result = await _complaintService.GetMyComplaintsAsync(userId.Value);
            return Ok(ApiResponse<System.Collections.Generic.List<ComplaintResponseDto>>.Ok(AdminIdentityRedaction.ForComplainant(result), "User complaints retrieved successfully."));
        }

        /// <summary>
        /// Marks a complaint as solved by its creator.
        /// </summary>
        [HttpPut("{id:guid}/solve")]
        [ProducesResponseType(typeof(ApiResponse<ComplaintResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SolveComplaint(Guid id, [FromBody] ReviewComplaintDto? request = null)
        {
            var userId = _currentUserService.UserId;
            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.Fail("User identity could not be retrieved from token."));
            }

            try
            {
                var result = await _complaintService.SolveComplaintAsync(id, userId.Value, request?.Notes);
                return Ok(ApiResponse<ComplaintResponseDto>.Ok(AdminIdentityRedaction.ForComplainant(result), "Complaint has been marked as solved."));
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
        /// Creator permanently deletes their complaint (any status) with its replies/audit log and activity reports.
        /// </summary>
        [HttpPut("{id:guid}/cancel")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CancelComplaint(Guid id)
        {
            var userId = _currentUserService.UserId;
            if (userId == null)
            {
                return Unauthorized(ApiResponse<object>.Fail("User identity could not be retrieved from token."));
            }

            try
            {
                await _complaintService.DeleteComplaintAsync(id, userId.Value);
                return Ok(ApiResponse<object>.Ok(null!, "Complaint deleted."));
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
