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
    [Authorize]
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
            var result = await _complaintService.CreateComplaintAsync(userId, request.HospitalId, request);

            return StatusCode(StatusCodes.Status201Created, ApiResponse<ComplaintResponseDto>.Ok(result, "Complaint submitted successfully."));
        }
    }
}
